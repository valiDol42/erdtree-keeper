using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ErdtreeKeeper.Core;
using ErdtreeKeeper.ViewModels;

namespace ErdtreeKeeper.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private bool _wired;

    /// <summary>
    /// Модель не знает про окна: диалоги она вызывает через эти делегаты.
    /// Так её логику можно проверить без запуска интерфейса.
    /// </summary>
    private void Wire()
    {
        if (ViewModel is not { } vm || _wired) return;
        _wired = true;

        // Сбой в любой команде не должен закрывать окно молча.
        AsyncRelayCommand.UnhandledError += ex => Dispatcher.UIThread.Post(async () =>
        {
            vm.Log.Error(Loc.Get("err.crashLog", ex.GetType().Name, ex.Message));
            await Dialogs.ReportAsync(this, Loc.Get("err.crashTitle"),
                Loc.Get("err.crashBody") + Environment.NewLine + Environment.NewLine
                + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine
                + (ex.StackTrace ?? ""));
        });

        vm.ConfirmAsync = (title, message, confirmText) =>
            Dialogs.ConfirmAsync(this, title, message, confirmText);

        vm.PromptAsync = (title, message, initial) =>
            Dialogs.PromptAsync(this, title, message, initial);

        vm.ShowReportAsync = (title, text) =>
            Dialogs.ReportAsync(this, title, text);

        vm.PickFolderAsync = PickFolderAsync;

        vm.SaveFileAsync = async (title, suggested) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggested,
                DefaultExtension = "txt",
            });

            return file?.Path.LocalPath;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    /// <summary>
    /// Ужимает окно под экран пользователя.
    ///
    /// Размер задан в логических единицах, а не в пикселях: на экране 1366x768
    /// или при масштабе 150 процентов окно 1180x900 оказывается больше рабочей
    /// области, и нижние кнопки уезжают под панель задач. Проверяем при
    /// открытии и на том мониторе, где окно действительно оказалось.
    /// </summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;

        // Небольшой отступ, чтобы окно не упиралось в края рабочей области.
        var maxWidth = screen.WorkingArea.Width / scale - 24;
        var maxHeight = screen.WorkingArea.Height / scale - 24;

        var width = Math.Max(MinWidth, Math.Min(Width, maxWidth));
        var height = Math.Max(MinHeight, Math.Min(Height, maxHeight));

        if (Math.Abs(width - Width) < 1 && Math.Abs(height - Height) < 1) return;

        Width = width;
        Height = height;

        // После изменения размера окно уже не по центру.
        Position = new PixelPoint(
            screen.WorkingArea.X + (int)((screen.WorkingArea.Width - width * scale) / 2),
            screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - height * scale) / 2));
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ViewModel is { } vm) await vm.LoadAsync();
    }

    private async void OnShowAutoSaveSettings(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        var window = Dialogs.CreateAutoSaveWindow(
            vm.AutoMinutes, vm.AutoKeep, vm.AutoFolder,
            minutes => vm.AutoMinutes = minutes,
            keep => vm.AutoKeep = keep,
            async () =>
            {
                var picked = await PickFolderAsync(Loc.Get("auto.pickFolder"), vm.AutoFolder);
                if (!string.IsNullOrWhiteSpace(picked)) vm.AutoFolder = picked;
                return vm.AutoFolder;
            });

        await window.ShowDialog(this);
    }

    /// <summary>Выбор папки. Общий для модели и для окна настроек автосохранения.</summary>
    private async Task<string?> PickFolderAsync(string title, string? start)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };

        if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
        {
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
        }

        var picked = await StorageProvider.OpenFolderPickerAsync(options);
        return picked.Count > 0 ? picked[0].Path.LocalPath : null;
    }

    private async void OnShowPlayerCard(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SaveContext is not { } context) return;
        await Dialogs.CreatePlayerCardWindow(context).ShowDialog(this);
    }

    private void OnOpenSite(object? sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Site);

    private void OnOpenHome(object? sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Home);

    private void OnOpenEmail(object? sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.EmailLink);

    private void OnHideLog(object? sender, RoutedEventArgs e)
    {
        var toggle = this.FindControl<ToggleButton>("LogToggle");
        if (toggle is not null) toggle.IsChecked = false;
    }

    private async void OnShowTransparency(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        await Dialogs.TransparencyAsync(this, vm.SettingsPath, vm.SnapshotFolder);
    }

    private async void OnShowAbout(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        await Dialogs.AboutAsync(this, vm.SettingsPath, vm.IsPortable, vm.SettingsFileState);
    }
}
