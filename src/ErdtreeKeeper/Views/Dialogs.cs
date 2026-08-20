using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ErdtreeKeeper.Core;

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
        var no = Action(Loc.Get("app.cancel"));

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
        var ok = Action(Loc.Get("app.save"), primary: true);
        var cancel = Action(Loc.Get("app.cancel"));

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
        var close = Action(Loc.Get("app.close"), primary: true);

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
        var close = Action(Loc.Get("app.close"), primary: true);

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading(Loc.Get("trans.title")),

                // Главное обещание вынесено наверх и подсвечено: с него
                // начинаются все вопросы недоверчивого игрока.
                NoNetworkBadge(),

                Section(Loc.Get("trans.reads"), Res<IBrush>("TextSecondaryBrush"),
                    Loc.Get("trans.readsWhat", Core.GameSaves.DefaultRoot)),

                Section(Loc.Get("trans.writes"), Res<IBrush>("AccentBrightBrush"),
                    Loc.Get("trans.writesWhat", snapshotFolder, settingsPath,
                        Core.SnapshotService.RestoreBackupFolder)),

                Section(Loc.Get("trans.notDoes"), Res<IBrush>("FreshBrush"),
                    Loc.Get("trans.notDoesWhat")),

                Section(Loc.Get("trans.howToCheck"), Res<IBrush>("TextSecondaryBrush"),
                    Loc.Get("trans.howToCheckWhat")),

                Buttons(close),
            },
        };

        var window = Shell(Loc.Get("trans.title"), 680, body);
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
                            Text = Loc.Get("trans.noNetwork"),
                            FontSize = 13,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Res<IBrush>("FreshBrush"),
                        },
                        new TextBlock
                        {
                            Text = Loc.Get("trans.noNetworkWhy"),
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
        var close = Action(Loc.Get("app.done"), primary: true);

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
        var browse = new Button { Content = Loc.Get("app.browse"), Classes = { "chip" } };
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
                Heading(Loc.Get("auto.title")),

                Body(Loc.Get("auto.explain")),

                Labelled(Loc.Get("auto.interval"), minutesBox, Loc.Get("auto.minutes")),
                Labelled(Loc.Get("auto.keep"), keepBox, Loc.Get("auto.pieces")),

                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = Loc.Get("auto.folder"), Classes = { "section" } },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children = { folderBox, browse },
                        },
                        new TextBlock
                        {
                            Text = Loc.Get("auto.folderHint"),
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

        var window = Shell(Loc.Get("auto.title"), 560, body);
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


    // ─── Карточка игрока ────────────────────────────────────────────────

    /// <summary>
    /// Всё, что удалось прочитать о персонаже.
    ///
    /// Данные только из самого сейва - ни одного запроса наружу. Уровень здесь
    /// же сверяется с суммой характеристик: если бы разбор структуры съехал,
    /// это было бы видно прямо в окне.
    /// </summary>
    public static Window CreatePlayerCardWindow(Core.SaveContext context)
    {
        var close = Action(Loc.Get("app.close"), primary: true);
        var slot = context.Character;

        var body = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                Heading(slot.Name),
                new TextBlock
                {
                    Text = Loc.Get("card.levelLine", slot.Level, slot.ClassName, slot.Index + 1),
                    Classes = { "body" },
                    Foreground = Res<IBrush>("AccentBrightBrush"),
                },

                StatGrid(slot.Stats),

                // Названия показателей отличаются от одноимённых характеристик:
                // "Здоровье" - вложенные очки, Loc.Get("card.hp") - что они дали.
                Facts(Loc.Get("card.vitals"),
                [
                    (Loc.Get("card.hp"), Number(slot.MaxHp)),
                    (Loc.Get("card.fp"), Number(slot.MaxFp)),
                    (Loc.Get("card.stamina"), Number(slot.MaxStamina)),
                ]),

                Facts(Loc.Get("card.progress"),
                [
                    (Loc.Get("card.runes"), Number(slot.Runes)),
                    (Loc.Get("card.runeMemory"), Number(slot.RuneMemory)),
                    (Loc.Get("card.played"), slot.PlayedText),
                ]),

                // Расхождение означало бы, что разбор структуры съехал. Молчать
                // об этом нельзя: рядом стоят числа, на которые игрок смотрит.
                slot.Level == slot.LevelFromStats
                    ? new Panel()
                    : Warning(Loc.Get("card.levelMismatch", slot.Level, slot.LevelFromStats)),

                context.IsDlc
                    ? Facts(Loc.Get("card.dlc"),
                    [
                        (Loc.Get("card.scadutree"), context.ScadutreeBlessing.ToString()),
                        (Loc.Get("card.spiritAsh"), context.SpiritAshBlessing.ToString()),
                    ])
                    : new Panel(),

                Facts(Loc.Get("card.where"),
                [
                    (Loc.Get("card.nearestGrace"), context.Location is null
                        ? Loc.Get("card.notFound")
                        : Loc.Get("card.distance", context.Location.Display, context.Location.Distance)),
                    (Loc.Get("card.nearestBoss"), context.Boss is null
                        ? Loc.Get("card.noneNearby")
                        : Loc.Get("card.distance", context.Boss.Display, context.Boss.Distance)),
                    (Loc.Get("card.map"), context.MapId.Length == 0 ? Loc.Get("card.unknown") : context.MapId),
                ]),

                Buttons(close),
            },
        };

        var window = Shell(Loc.Get("card.title"), 560, body);
        close.Click += (_, _) => window.Close();
        return window;
    }

    /// <summary>
    /// Число с пробелом между разрядами.
    ///
    /// Стандартное форматирование дало бы запятые: у собранного файла включён
    /// InvariantGlobalization, и правила локали до него не доходят.
    /// </summary>
    private static string Number(long value)
    {
        var format = (System.Globalization.NumberFormatInfo)
            System.Globalization.CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSeparator = Loc.Current.IsEnglish ? "," : " ";
        format.NumberDecimalDigits = 0;
        return value.ToString("N", format);
    }

    private static Control Warning(string text) => new Border
    {
        Classes = { "inset" },
        Background = Res<IBrush>("DangerWashBrush"),
        BorderBrush = Res<IBrush>("DangerBrush"),
        Child = new TextBlock
        {
            Text = text,
            Classes = { "secondary" },
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res<IBrush>("DangerBrush"),
        },
    };

    /// <summary>Восемь характеристик в две колонки.</summary>
    private static Control StatGrid(Core.CharacterStats stats)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };

        var index = 0;
        foreach (var (name, value) in stats.All)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 0, index % 2 == 0 ? 14 : 0, 8),
            };

            var label = new TextBlock
            {
                Text = name,
                Classes = { "secondary" },
                VerticalAlignment = VerticalAlignment.Center,
            };

            var number = new TextBlock
            {
                Text = value.ToString(),
                Classes = { "title" },
                Foreground = Res<IBrush>("TextPrimaryBrush"),
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(number, 1);
            row.Children.Add(label);
            row.Children.Add(number);

            Grid.SetColumn(row, index % 2);
            Grid.SetRow(row, index / 2);
            grid.Children.Add(row);
            index++;
        }

        return new Border { Classes = { "inset" }, Child = grid };
    }

    /// <summary>Блок "название - значение" под общим заголовком.</summary>
    private static Control Facts(string title, (string Name, string Value)[] rows)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(new TextBlock { Text = title, Classes = { "section" } });

        foreach (var (name, value) in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var label = new TextBlock { Text = name, Classes = { "secondary" } };
            var text = new TextBlock
            {
                Text = value,
                Classes = { "body" },
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(label);
            grid.Children.Add(text);
            panel.Children.Add(grid);
        }

        return panel;
    }

    // ─── О программе ────────────────────────────────────────────────────

    /// <summary>О программе: версия, пути и контрольная сумма самого файла.</summary>
    public static Window CreateAboutWindow(string settingsPath, bool portable, string fileState)
    {
        var close = Action(Loc.Get("app.close"), primary: true);

        var hashLine = new SelectableTextBlock
        {
            Text = Loc.Get("about.hashPrompt"),
            Classes = { "mono" },
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res<IBrush>("TextSecondaryBrush"),
        };

        var hashButton = new Button { Content = Loc.Get("about.hashButton"), Classes = { "chip" } };
        hashButton.Click += async (_, _) =>
        {
            hashLine.Text = Loc.Get("about.hashWorking");
            hashButton.IsEnabled = false;
            hashLine.Text = await Task.Run(SelfHash);
        };

        var body = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                Heading(AppInfo.Name),
                Body(Loc.Get("about.version", AppInfo.FullVersion)),

                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = Loc.Get("about.contact"), Classes = { "section" } },
                        Link(ExternalLinks.Email, ExternalLinks.EmailLink),
                        new TextBlock
                        {
                            Text = Loc.Get("about.contactWhat"),
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
                        new TextBlock { Text = Loc.Get("about.site"), Classes = { "section" } },
                        Link(ExternalLinks.SiteLabel, ExternalLinks.Site),
                        new TextBlock
                        {
                            Text = Loc.Get("about.siteWhat"),
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
                        new TextBlock { Text = Loc.Get("about.source"), Classes = { "section" } },
                        Link(ExternalLinks.RepositoryLabel, ExternalLinks.Repository),
                        new TextBlock
                        {
                            Text = Loc.Get("about.sourceWhat"),
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },

                // Пути не зашиты: папка программы берётся у самого процесса,
                // а состояние файла настроек читается с диска при открытии окна.
                Section(Loc.Get("about.where"), Res<IBrush>("TextSecondaryBrush"),
                    Loc.Get("about.whereWhat", Core.PortableSettings.AppFolder, settingsPath, fileState,
                        Loc.Get(portable ? "about.portable" : "about.notPortable"))),

                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = Loc.Get("about.hash"), Classes = { "section" } },
                        new TextBlock
                        {
                            Text = Loc.Get("about.hashWhy"),
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

        var window = Shell(Loc.Get("about.title"), 640, body);
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
            if (string.IsNullOrEmpty(path)) return Loc.Get("about.hashNoPath");

            using var stream = File.OpenRead(path);
            var hash = System.Security.Cryptography.SHA256.HashData(stream);
            return Convert.ToHexStringLower(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Get("about.hashFailed", ex.Message);
        }
    }
}
