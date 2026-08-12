using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Диалоги собираются кодом, а не разметкой: их немного, они однотипные, и так
/// они гарантированно наследуют общие стили приложения.
/// </summary>
public static class Dialogs
{
    private static Window Shell(string title, double width, Control body)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Res<IBrush>("AppBackground"),
        };

        window.Content = new Border
        {
            Padding = new Thickness(24),
            Child = body,
        };

        return window;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        Classes = { "display" },
        Margin = new Thickness(0, 0, 0, 12),
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        Classes = { "body" },
        Foreground = Res<IBrush>("TextSecondaryBrush"),
    };

    private static Button Action(string text, bool primary = false, bool danger = false)
    {
        var button = new Button { Content = text, MinWidth = 110 };
        if (primary) button.Classes.Add("primary");
        if (danger) button.Classes.Add("danger");
        return button;
    }

    private static T? Res<T>(string key) where T : class
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as T
            : null;
    }

    /// <summary>Подтверждение действия. Кнопка согласия подписана глаголом, а не "ОК".</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText)
    {
        var result = false;

        var yes = Action(confirmText, danger: true);
        var no = Action("Отмена");

        var body = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new StackPanel
                {
                    Spacing = 10,
                    Children = { Heading(title), Body(message) },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };

        var window = Shell(title, 520, body);
        yes.Click += (_, _) => { result = true; window.Close(); };
        no.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
        return result;
    }

    /// <summary>Ввод одной строки.</summary>
    public static async Task<string?> PromptAsync(Window owner, string title, string message, string initial)
    {
        string? result = null;

        var input = new TextBox { Text = initial };
        var ok = Action("Сохранить", primary: true);
        var cancel = Action("Отмена");

        var body = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new StackPanel
                {
                    Spacing = 10,
                    Children = { Heading(title), Body(message), input },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };

        var window = Shell(title, 520, body);
        ok.Click += (_, _) => { result = input.Text ?? ""; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { result = input.Text ?? ""; window.Close(); }
        };

        window.Opened += (_, _) => { input.SelectAll(); input.Focus(); };

        await window.ShowDialog(owner);
        return result;
    }

    /// <summary>Показ длинного текстового отчёта моноширинным шрифтом.</summary>
    public static async Task ReportAsync(Window owner, string title, string text)
    {
        var close = Action("Закрыть", primary: true);

        var body = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                Heading(title),
                new Border
                {
                    Classes = { "inset" },
                    MaxHeight = 460,
                    Child = new ScrollViewer
                    {
                        Content = new SelectableTextBlock
                        {
                            Text = text,
                            Classes = { "mono" },
                            Foreground = Res<IBrush>("TextPrimaryBrush"),
                        },
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };

        var window = Shell(title, 640, body);
        close.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
    }

    /// <summary>
    /// Полный перечень того, что программа делает с диском.
    ///
    /// Живёт отдельным окном и открывается одной кнопкой из шапки: игроку,
    /// который боится вирусов, ответ должен быть доступен сразу, а не в
    /// документации где-то на сайте.
    /// </summary>
    public static async Task TransparencyAsync(Window owner, string settingsPath, string snapshotFolder)
    {
        var close = Action("Закрыть", primary: true);

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading("Что программа делает"),

                Section("Читает", Res<IBrush>("TextSecondaryBrush"),
                    $"Папку сохранений {Core.GameSaves.DefaultRoot} и файлы .sl2 и .co2 внутри неё. "
                    + "Файлы открываются только на чтение и в режиме, который не мешает игре."),

                Section("Пишет", Res<IBrush>("AccentBrightBrush"),
                    $"Снимки в папку {snapshotFolder}.\n"
                    + $"Настройки в файл {settingsPath}.\n"
                    + "В папку игры программа пишет ровно один раз - когда вы нажимаете "
                    + "\"Восстановить в игру\". Перед этим текущий сейв всегда уезжает в "
                    + $"подпапку \"{Core.SnapshotService.RestoreBackupFolder}\"."),

                Section("Не делает", Res<IBrush>("FreshBrush"),
                    "Не обращается в интернет - ни за обновлениями, ни со статистикой.\n"
                    + "Не меняет содержимое сохранений: копируется файл целиком, байт в байт.\n"
                    + "Не просит прав администратора.\n"
                    + "Не прописывается в автозагрузку и не остаётся в памяти после закрытия.\n"
                    + "Не трогает файлы игры, реестр и системные настройки."),

                Section("Как проверить", Res<IBrush>("TextSecondaryBrush"),
                    "Журнал операций внизу окна показывает каждое обращение к диску, и его "
                    + "можно выгрузить в текстовый файл.\n"
                    + "Отсутствие сети проверяется любым монитором соединений - у программы "
                    + "нет ни одной сетевой библиотеки.\n"
                    + "Исходный код открыт: собранный файл можно сверить по контрольной сумме "
                    + "в окне \"О программе\"."),

                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };

        var window = Shell("Что программа делает", 660, body);
        close.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
    }

    private static Control Section(string title, IBrush? accent, string text) => new StackPanel
    {
        Spacing = 6,
        Children =
        {
            new TextBlock
            {
                Text = title.ToUpperInvariant(),
                Classes = { "section" },
                Foreground = accent,
            },
            new SelectableTextBlock
            {
                Text = text,
                Classes = { "secondary" },
                Foreground = Res<IBrush>("TextSecondaryBrush"),
            },
        },
    };

    /// <summary>О программе: версия, пути и контрольная сумма самого файла.</summary>
    public static async Task AboutAsync(Window owner, string version, string settingsPath, bool portable)
    {
        var close = Action("Закрыть", primary: true);
        var hashLine = new SelectableTextBlock
        {
            Text = "нажмите, чтобы посчитать",
            Classes = { "mono" },
            Foreground = Res<IBrush>("TextSecondaryBrush"),
        };
        var hashButton = new Button { Content = "Посчитать SHA-256", Classes = { "chip" } };

        hashButton.Click += async (_, _) =>
        {
            hashLine.Text = "считаю...";
            hashButton.IsEnabled = false;
            hashLine.Text = await Task.Run(SelfHash);
        };

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading("Erdtree Keeper"),
                Body($"Версия {version}. Хранитель сохранений Elden Ring."),

                Section("Где что лежит", Res<IBrush>("TextSecondaryBrush"),
                    $"Программа: {Core.PortableSettings.AppFolder}\n"
                    + $"Настройки: {settingsPath}\n"
                    + (portable
                        ? "Настройки лежат рядом с программой - её можно носить на флешке."
                        : "Папка программы недоступна для записи, поэтому настройки ушли в AppData.")),

                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "КОНТРОЛЬНАЯ СУММА ЭТОГО ФАЙЛА",
                            Classes = { "section" },
                        },
                        new TextBlock
                        {
                            Text = "Сверьте её с суммой, опубликованной на странице релиза: "
                                   + "совпадение означает, что файл не подменяли.",
                            Classes = { "muted" },
                        },
                        hashButton,
                        hashLine,
                    },
                },

                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };

        var window = Shell("О программе", 620, body);
        close.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
    }

    private static string SelfHash()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return "не удалось определить путь к файлу";

            using var stream = File.OpenRead(path);
            var hash = System.Security.Cryptography.SHA256.HashData(stream);
            return Convert.ToHexStringLower(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"не удалось посчитать: {ex.Message}";
        }
    }
}
