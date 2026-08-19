using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Диалоги собираются кодом, а не разметкой: их немного, они однотипные, и так
/// они гарантированно наследуют общие стили приложения.
///
/// Каждое окно можно создать отдельно от показа - на этом держится проверка
/// вида в tools/UiPreview: окно рисуется в файл, не открываясь на рабочем столе.
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
            MaxHeight = 820,
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
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };

    // Перенос ставится и здесь, хотя он есть в теме: у диалогов ширина
    // фиксированная, и строка без переноса просто уезжает за край окна.
    private static SelectableTextBlock Body(string text) => new()
    {
        Text = text,
        Classes = { "body" },
        TextWrapping = TextWrapping.Wrap,
        Foreground = Res<IBrush>("TextSecondaryBrush"),
    };

    /// <summary>Ссылка наружу. Открывает браузер, ничего не качает сама.</summary>
    private static Button Link(string label, string url)
    {
        var button = new Button
        {
            Content = label,
            Classes = { "link" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 2),
        };

        button.Click += (_, _) => ExternalLinks.Open(url);
        return button;
    }

    private static Button Action(string text, bool primary = false, bool danger = false)
    {
        var button = new Button { Content = text, MinWidth = 110 };
        if (primary) button.Classes.Add("primary");
        if (danger) button.Classes.Add("danger");
        return button;
    }

    private static StackPanel Buttons(params Control[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    private static T? Res<T>(string key) where T : class
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as T
            : null;
    }

    // ─── Подтверждение и ввод ───────────────────────────────────────────

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
                new StackPanel { Spacing = 10, Children = { Heading(title), Body(message) } },
                Buttons(no, yes),
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
                new StackPanel { Spacing = 10, Children = { Heading(title), Body(message), input } },
                Buttons(cancel, ok),
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

    // ─── Отчёт ──────────────────────────────────────────────────────────

    /// <summary>
    /// Длинный текстовый отчёт моноширинным шрифтом.
    ///
    /// Перенос здесь выключен намеренно: отчёт о целостности - таблица, и
    /// перенос сломал бы выравнивание столбцов. Вместо этого он прокручивается.
    /// </summary>
    public static Window CreateReportWindow(string title, string text)
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
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = new SelectableTextBlock
                        {
                            Text = text,
                            Classes = { "mono" },
                            TextWrapping = TextWrapping.NoWrap,
                            Foreground = Res<IBrush>("TextPrimaryBrush"),
                        },
                    },
                },
                Buttons(close),
            },
        };

        var window = Shell(title, 660, body);
        close.Click += (_, _) => window.Close();
        return window;
    }

    public static Task ReportAsync(Window owner, string title, string text) =>
        CreateReportWindow(title, text).ShowDialog(owner);

    // ─── Что программа делает ───────────────────────────────────────────

    /// <summary>
    /// Полный перечень того, что программа делает с диском.
    ///
    /// Открывается одной кнопкой из шапки: игроку, который боится вирусов,
    /// ответ должен быть доступен сразу, а не в документации где-то на сайте.
    ///
    /// Пути подставляются настоящие, вычисленные на этом компьютере, - в них
    /// видно имя текущего пользователя Windows, потому что ничего не зашито.
    /// </summary>
    public static Window CreateTransparencyWindow(string settingsPath, string snapshotFolder)
    {
        var close = Action("Закрыть", primary: true);

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading("Что программа делает"),

                // Главное обещание вынесено наверх и подсвечено: с него
                // начинаются все вопросы недоверчивого игрока.
                NoNetworkBadge(),

                Section("Читает", Res<IBrush>("TextSecondaryBrush"),
                    $"Папку сохранений {Core.GameSaves.DefaultRoot} и файлы .sl2 и .co2 внутри неё. "
                    + "Файлы открываются только на чтение и в режиме, который не мешает игре."),

                Section("Пишет", Res<IBrush>("AccentBrightBrush"),
                    $"Снимки в папку {snapshotFolder}\n"
                    + $"Настройки в файл {settingsPath}\n\n"
                    + "В папку игры программа пишет ровно один раз - когда вы нажимаете "
                    + "\"Восстановить в игру\". Перед этим текущий сейв всегда уезжает в "
                    + $"подпапку \"{Core.SnapshotService.RestoreBackupFolder}\"."),

                Section("Не делает", Res<IBrush>("FreshBrush"),
                    "Не обращается в интернет - ни за обновлениями, ни со статистикой. "
                    + "Ссылка на сайт проекта - единственное исключение, и работает она "
                    + "не сама: по щелчку программа просит систему открыть браузер, "
                    + "а соединение устанавливает уже он.\n"
                    + "Не меняет содержимое сохранений: копируется файл целиком, байт в байт.\n"
                    + "Не просит прав администратора.\n"
                    + "Не прописывается в автозагрузку и не остаётся в памяти после закрытия.\n"
                    + "Не изменяет файлы игры, реестр и системные настройки."),

                Section("Как проверить", Res<IBrush>("TextSecondaryBrush"),
                    "Журнал операций показывает каждое обращение к диску, и его "
                    + "можно выгрузить в текстовый файл.\n"
                    + "Отсутствие сети проверяется любым монитором соединений - в готовом "
                    + "файле нет ни одной сетевой библиотеки.\n"
                    + "Исходный код открыт: собранный файл можно сверить по контрольной сумме "
                    + "в окне \"О программе\"."),

                Buttons(close),
            },
        };

        var window = Shell("Что программа делает", 680, body);
        close.Click += (_, _) => window.Close();
        return window;
    }

    public static Task TransparencyAsync(Window owner, string settingsPath, string snapshotFolder) =>
        CreateTransparencyWindow(settingsPath, snapshotFolder).ShowDialog(owner);

    /// <summary>Отметка о том, что программа не открывает соединений.</summary>
    private static Control NoNetworkBadge() => new Border
    {
        Background = Res<IBrush>("FreshWashBrush"),
        BorderBrush = Res<IBrush>("FreshBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 11),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Res<IBrush>("FreshBrush"),
                    // По верхней строке, а не по центру блока: иначе точка
                    // оказывается напротив пояснения, а не заголовка.
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 0, 0),
                },
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Не выходит в интернет",
                            FontSize = 13,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Res<IBrush>("FreshBrush"),
                        },
                        new TextBlock
                        {
                            Text = "В готовом файле нет ни одной сетевой библиотеки - "
                                   + "открыть соединение программа технически не может.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 540,
                        },
                    },
                },
            },
        },
    };

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
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res<IBrush>("TextSecondaryBrush"),
            },
        },
    };


    // ─── Настройки автосохранения ───────────────────────────────────────

    /// <summary>
    /// Настройки автосохранения отдельным окном.
    ///
    /// На главном экране они занимали место, которого при невысоком окне не
    /// хватало, и строка с папкой уезжала за край. Настраивают их один раз, а
    /// смотрят на главный экран постоянно - поэтому здесь.
    /// </summary>
    public static Window CreateAutoSaveWindow(
        int minutes, int keep, string folder,
        Action<int> onMinutes, Action<int> onKeep, Func<Task<string?>> pickFolder)
    {
        var close = Action("Готово", primary: true);

        var minutesBox = new NumericUpDown
        {
            Minimum = 1, Maximum = 120, Increment = 1,
            FormatString = "0", Value = minutes, Width = 120,
        };
        minutesBox.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v) onMinutes((int)v);
        };

        var keepBox = new NumericUpDown
        {
            Minimum = 1, Maximum = 200, Increment = 1,
            FormatString = "0", Value = keep, Width = 120,
        };
        keepBox.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v) onKeep((int)v);
        };

        var folderBox = new TextBox { Text = folder, IsReadOnly = true };
        var browse = new Button { Content = "Обзор...", Classes = { "chip" } };
        browse.Click += async (_, _) =>
        {
            var picked = await pickFolder();
            if (!string.IsNullOrWhiteSpace(picked)) folderBox.Text = picked;
        };

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading("Автосохранение"),

                Body("Снимок делается по факту записи сейва игрой, а не по расписанию: "
                     + "копия, снятая в момент записи, была бы обрывком. Указанные минуты - "
                     + "нижняя граница частоты, чаще этого снимок не сработает."),

                Labelled("Не чаще одного снимка в", minutesBox, "мин"),
                Labelled("Хранить последних", keepBox, "шт"),

                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "ПАПКА ДЛЯ АВТОСОХРАНЕНИЙ", Classes = { "section" } },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children = { folderBox, browse },
                        },
                        new TextBlock
                        {
                            Text = "Можно указать другой диск - тогда копии переживут "
                                   + "переустановку системы.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },

                Buttons(close),
            },
        };

        Grid.SetColumn(browse, 1);
        browse.Margin = new Thickness(8, 0, 0, 0);

        var window = Shell("Автосохранение", 560, body);
        close.Click += (_, _) => window.Close();
        return window;
    }

    /// <summary>Подпись, поле и единица измерения в одной строке.</summary>
    private static Control Labelled(string label, Control control, string suffix)
    {
        var text = new TextBlock
        {
            Text = label,
            Classes = { "body" },
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res<IBrush>("TextSecondaryBrush"),
        };

        var unit = new TextBlock
        {
            Text = suffix,
            Classes = { "secondary" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        // Колонку задаём каждому явно: без этого все дети попадают в первую и
        // накладываются друг на друга.
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        Grid.SetColumn(unit, 2);

        grid.Children.Add(text);
        grid.Children.Add(control);
        grid.Children.Add(unit);
        return grid;
    }

    // ─── О программе ────────────────────────────────────────────────────

    /// <summary>О программе: версия, пути и контрольная сумма самого файла.</summary>
    public static Window CreateAboutWindow(string settingsPath, bool portable, string fileState)
    {
        var close = Action("Закрыть", primary: true);

        var hashLine = new SelectableTextBlock
        {
            Text = "нажмите кнопку, чтобы посчитать",
            Classes = { "mono" },
            TextWrapping = TextWrapping.Wrap,
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
                Heading(AppInfo.Name),
                Body($"Версия {AppInfo.FullVersion}. Хранитель файлов сохранения Elden Ring.\n"
                     + "Лицензия MIT."),

                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "СВЯЗЬ", Classes = { "section" } },
                        Link(ExternalLinks.Email, ExternalLinks.EmailLink),
                        new TextBlock
                        {
                            Text = "Вопросы, ошибки и всё, что касается безопасности - сюда.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },

                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "САЙТ ПРОЕКТА", Classes = { "section" } },
                        Link(ExternalLinks.SiteLabel, ExternalLinks.Site),
                        new TextBlock
                        {
                            Text = "Карта и трекер прогресса Elden Ring. Оттуда же взят справочник "
                                   + "мест благодати и арен боссов, по которому программа называет снимки.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },

                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "ИСХОДНЫЙ КОД", Classes = { "section" } },
                        Link(ExternalLinks.RepositoryLabel, ExternalLinks.Repository),
                        new TextBlock
                        {
                            Text = "Всё, что делает программа, можно прочитать целиком. Там же "
                                   + "лежат контрольные суммы релизов и подтверждение того, что "
                                   + "выложенный файл собран именно из этого кода.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },

                // Пути не зашиты: папка программы берётся у самого процесса,
                // а состояние файла настроек читается с диска при открытии окна.
                Section("Где что лежит", Res<IBrush>("TextSecondaryBrush"),
                    $"Программа: {Core.PortableSettings.AppFolder}\n\n"
                    + $"Настройки: {settingsPath}\n"
                    + $"Сейчас на диске: {fileState}\n\n"
                    + (portable
                        ? "Настройки лежат рядом с программой - её можно носить на флешке."
                        : "Папка программы недоступна для записи, поэтому настройки ушли в AppData.")),

                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "КОНТРОЛЬНАЯ СУММА ЭТОГО ФАЙЛА", Classes = { "section" } },
                        new TextBlock
                        {
                            Text = "Сверьте её с суммой, опубликованной на странице релиза: "
                                   + "совпадение означает, что файл не подменяли.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                        hashButton,
                        hashLine,
                    },
                },

                Buttons(close),
            },
        };

        var window = Shell("О программе", 640, body);
        close.Click += (_, _) => window.Close();
        return window;
    }

    public static Task AboutAsync(Window owner, string settingsPath, bool portable, string fileState) =>
        CreateAboutWindow(settingsPath, portable, fileState).ShowDialog(owner);

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
