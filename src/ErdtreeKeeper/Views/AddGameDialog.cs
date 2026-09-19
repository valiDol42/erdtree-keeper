using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ErdtreeKeeper.Core;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Окно добавления игры.
///
/// Два пути к одному и тому же. Первый - выбрать из списка установленных игр
/// Steam: у тех, что хранят сохранения в облаке, папка уже известна, и
/// указывать вручную нечего. Второй - показать папку самому: так добавляются
/// игры не из Steam и те, чьи сейвы лежат вне облака.
///
/// Список читается из файлов самого Steam на диске. Никаких запросов наружу
/// здесь нет - и быть не может: программа выходит в сеть только за
/// обновлениями и только с разрешения.
/// </summary>
public static class AddGameDialog
{
    /// <summary>Строка списка игр Steam.</summary>
    private sealed class GameRow(SteamGame game)
    {
        public SteamGame Game { get; } = game;

        public string Display => $"{Game.Name}   ·   {Loc.Get("addgame.cloud")}";
    }

    /// <summary>
    /// Создаёт окно. Результат кладётся в <paramref name="result"/> - окно
    /// собирается отдельно от показа, чтобы его можно было нарисовать в файл
    /// без рабочего стола.
    /// </summary>
    public static Window Create(
        Func<string, string?, Task<string?>> pickFolder,
        Action<CustomGame?> result)
    {
        var name = new TextBox { PlaceholderText = Loc.Get("addgame.namePlaceholder") };
        var folder = new TextBox { IsReadOnly = true };
        var extensions = new TextBox { PlaceholderText = ".sav, .dat" };
        var process = new TextBox { PlaceholderText = "game.exe" };

        var browse = new Button { Content = Loc.Get("app.browse"), Classes = { "chip" } };
        var found = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var problem = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Foreground = Dialogs.Res<IBrush>("DangerBrush"),
            IsVisible = false,
        };

        var steamList = new ListBox
        {
            MaxHeight = 190,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<GameRow>((row, _) =>
                new TextBlock { Text = row?.Display ?? "", TextTrimming = TextTrimming.CharacterEllipsis }),
        };

        var steamHint = new TextBlock
        {
            Text = Loc.Get("addgame.steamHint"),
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        };

        var add = Dialogs.Action(Loc.Get("addgame.add"), primary: true);
        var cancel = Dialogs.Action(Loc.Get("app.cancel"));

        var body = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                Dialogs.Heading(Loc.Get("addgame.title")),
                Dialogs.Body(Loc.Get("addgame.lead")),

                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = Loc.Get("addgame.steam"), Classes = { "section" } },
                        steamHint,
                        steamList,
                    },
                },

                new Border { Classes = { "divider" } },

                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = Loc.Get("addgame.manual"), Classes = { "section" } },
                        Field(Loc.Get("addgame.name"), name),
                        Field(Loc.Get("addgame.folder"), Row(folder, browse)),
                        found,
                        Field(Loc.Get("addgame.extensions"), extensions, Loc.Get("addgame.extensionsHint")),
                        Field(Loc.Get("addgame.process"), process, Loc.Get("addgame.processHint")),
                        problem,
                    },
                },

            },
        };

        // Кнопки живут вне прокрутки: внутри неё они уезжали за нижний край
        // окна вместе с содержимым, и кнопку "Добавить" приходилось искать.
        var window = Dialogs.Shell(Loc.Get("addgame.title"), 620, new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                Docked(Dialogs.Buttons(cancel, add), Dock.Bottom, new Thickness(0, 16, 0, 0)),
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 620,
                    // Отступ справа: без него поля ввода уходят под полосу
                    // прокрутки и выглядят обрезанными.
                    Padding = new Thickness(0, 0, 10, 0),
                    Content = body,
                },
            },
        });

        // Список ищется на диске: библиотек Steam бывает несколько, и на
        // медленном диске перебор заметен. Окно при этом должно открыться
        // сразу, поэтому поиск идёт своим чередом.
        _ = LoadSteamGamesAsync(steamList, steamHint);

        steamList.SelectionChanged += (_, _) =>
        {
            if (steamList.SelectedItem is not GameRow row) return;

            name.Text = row.Game.Name;
            folder.Text = row.Game.CloudFolders.Count > 0 ? row.Game.CloudFolders[0] : "";
            UpdateFound(found, folder.Text, extensions.Text);
        };

        browse.Click += async (_, _) =>
        {
            var picked = await pickFolder(Loc.Get("addgame.folder"), folder.Text);
            if (string.IsNullOrWhiteSpace(picked)) return;

            folder.Text = picked;
            steamList.SelectedItem = null;

            // Название подставляем из имени папки: чаще всего оно и есть
            // название игры, а исправить его всегда можно руками.
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileName(picked.TrimEnd(
                Path.DirectorySeparatorChar));

            UpdateFound(found, folder.Text, extensions.Text);
        };

        extensions.TextChanged += (_, _) => UpdateFound(found, folder.Text, extensions.Text);

        add.Click += (_, _) =>
        {
            var problemText = Validate(name.Text, folder.Text);
            if (problemText is not null)
            {
                problem.Text = problemText;
                problem.IsVisible = true;
                return;
            }

            var steamGame = (steamList.SelectedItem as GameRow)?.Game;

            result(new CustomGame
            {
                Id = GameProfiles.NewCustomId(),
                Name = name.Text!.Trim(),
                Folder = folder.Text!.Trim(),
                IsSteamCloud = steamGame is not null,
                AppId = steamGame?.AppId,
                Extensions = extensions.Text?.Trim(),
                Process = CleanProcess(process.Text),
            });

            window.Close();
        };

        cancel.Click += (_, _) => window.Close();
        return window;
    }

    /// <summary>Показывает окно и возвращает добавленную игру или ничего.</summary>
    public static async Task<CustomGame?> ShowAsync(
        Window owner, Func<string, string?, Task<string?>> pickFolder)
    {
        CustomGame? added = null;
        var window = Create(pickFolder, game => added = game);
        await window.ShowDialog(owner);
        return added;
    }

    private static async Task LoadSteamGamesAsync(ListBox list, TextBlock hint)
    {
        var games = await Task.Run(() => SteamLibrary.InstalledGames());
        var withCloud = games.Where(g => g.HasCloudSaves).ToList();

        if (withCloud.Count > 0)
        {
            list.ItemsSource = withCloud.Select(g => new GameRow(g)).ToList();
            return;
        }

        list.IsVisible = false;
        hint.Text = games.Count == 0 ? Loc.Get("addgame.noSteam") : Loc.Get("addgame.noCloudGames");
    }

    /// <summary>
    /// Сколько файлов программа увидит в этой папке.
    ///
    /// Показывается до добавления, а не после: пустой список сохранений в
    /// главном окне ничего не объясняет, а цифра здесь сразу говорит, туда ли
    /// указали папку.
    /// </summary>
    private static void UpdateFound(TextBlock label, string? folder, string? extensions)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            label.Text = "";
            return;
        }

        var probe = new GameProfile("probe", "probe", SaveRootKind.Custom, folder,
            GameProfiles.SplitExtensions(extensions), null, [], SaveLayout.Opaque, BuiltIn: false);

        var count = 0;
        foreach (var account in GameSaves.FindAccounts(probe, folder))
        {
            count += GameSaves.FindSaveFiles(probe, account.Path).Count;
        }

        label.Text = count > 0 ? Loc.Get("addgame.filesFound", count) : Loc.Get("addgame.filesNone");
    }

    private static string? Validate(string? name, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Loc.Get("addgame.needFolder");

        if (string.IsNullOrWhiteSpace(name)) return Loc.Get("addgame.needName");

        // Папка уже известной игры означала бы две записи об одном и том же, а
        // с ними - две папки снимков и путаница в том, какая из них чья.
        if (GameSaves.IsInsideGameFolder(folder)) return Loc.Get("addgame.insideKnown");

        return null;
    }

    /// <summary>Имя процесса без ".exe" - системе оно нужно именно так.</summary>
    private static string? CleanProcess(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text[..^4] : text;
    }

    private static Control Docked(Control control, Dock side, Thickness margin)
    {
        control.Margin = margin;
        DockPanel.SetDock(control, side);
        return control;
    }

    private static Control Row(Control left, Control right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        grid.ColumnSpacing = 8;
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Control Field(string label, Control input, string? hint = null)
    {
        var panel = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = label, Classes = { "secondary" } },
                input,
            },
        };

        if (hint is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return panel;
    }
}
