using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

    /// <summary>
    /// Модель не знает про окна: диалоги она вызывает через эти делегаты.
    /// Так её логику можно проверить без запуска интерфейса.
    /// </summary>
    private void Wire()
    {
        if (ViewModel is not { } vm) return;

        vm.ConfirmAsync = (title, message, confirmText) =>
            Dialogs.ConfirmAsync(this, title, message, confirmText);

        vm.PromptAsync = (title, message, initial) =>
            Dialogs.PromptAsync(this, title, message, initial);

        vm.ShowReportAsync = (title, text) =>
            Dialogs.ReportAsync(this, title, text);

        vm.PickFolderAsync = async (title, start) =>
        {
            var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };

            if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
            {
                options.SuggestedStartLocation =
                    await StorageProvider.TryGetFolderFromPathAsync(start);
            }

            var picked = await StorageProvider.OpenFolderPickerAsync(options);
            return picked.Count > 0 ? picked[0].Path.LocalPath : null;
        };

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

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ViewModel is { } vm) await vm.LoadAsync();
    }

    private void OnOpenSite(object? sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Site);

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

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0.0";

        // У сборки версия приходит с хешем коммита после "+": в окне он лишний.
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        await Dialogs.AboutAsync(this, version, vm.SettingsPath, vm.IsPortable, vm.SettingsFileState);
    }
}
