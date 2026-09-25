using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ErdtreeKeeper.Core;
using ErdtreeKeeper.Updates;
using ErdtreeKeeper.ViewModels;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Окно обновлений - единственное место, откуда программа выходит в сеть, и
/// единственное, откуда она может себя заменить.
///
/// Поэтому оно устроено как разговор, а не как кнопка "обновить". Сначала
/// спрашивается разрешение, и до ответа не уходит ни один запрос. Дальше на
/// каждом шаге видно, что именно произошло: какой выпуск нашёлся, что
/// скачано, сошлась ли контрольная сумма. Ничего не ставится молча.
/// </summary>
public static class UpdateDialog
{
    public static Window Create(MainViewModel model, Action shutdown) =>
        Create(model, shutdown, preview: null, installedVersion: null);

    /// <summary>
    /// Окно, открытое сразу на найденном выпуске, - без запроса в сеть.
    ///
    /// Нужно инструменту снимков экрана: показать, как выглядит предложение
    /// обновиться, можно только имея выпуск новее установленного, а ждать
    /// его ради картинки на сайте незачем. Выпуск передаётся настоящий, со
    /// своими заметками, - меняется только то, какая версия считается
    /// установленной.
    /// </summary>
    public static Window CreatePreview(MainViewModel model, ReleaseInfo release, string installedVersion) =>
        Create(model, () => { }, release, installedVersion);

    private static Window Create(MainViewModel model, Action shutdown, ReleaseInfo? preview, string? installedVersion)
    {
        var content = new StackPanel { Spacing = 14 };
        var close = Dialogs.Action(Loc.Get("app.close"));

        var window = Dialogs.Shell(Loc.Get("upd.title"), 620, new StackPanel
        {
            Spacing = 16,
            Children =
            {
                Dialogs.Heading(Loc.Get("upd.title")),
                new TextBlock
                {
                    Text = Loc.Get("upd.current", installedVersion ?? AppInfo.FullVersion),
                    Classes = { "secondary" },
                },
                content,
                Dialogs.Buttons(close),
            },
        });

        close.Click += (_, _) => window.Close();

        var state = new DialogState(model, window, content, shutdown);
        if (preview is null) state.ShowStart();
        else state.ShowRelease(preview);

        return window;
    }

    public static Task ShowAsync(Window owner, MainViewModel model, Action shutdown) =>
        Create(model, shutdown).ShowDialog(owner);

    /// <summary>
    /// Состояние разговора. Вынесено в класс, потому что шагов несколько и
    /// каждый перерисовывает середину окна, оставляя заголовок и кнопку на
    /// месте.
    /// </summary>
    private sealed class DialogState(MainViewModel model, Window window, StackPanel content, Action shutdown)
    {
        private readonly CancellationTokenSource _cancel = new();

        public void ShowStart()
        {
            window.Closed += (_, _) => _cancel.Cancel();

            if (model.UpdatesAllowed == true) ShowAllowed();
            else ShowPermissionRequest();
        }

        /// <summary>Разрешения нет: объясняем, что именно произойдёт, и спрашиваем.</summary>
        private void ShowPermissionRequest()
        {
            var onStart = new CheckBox
            {
                Content = Loc.Get("upd.checkOnStart"),
                IsChecked = model.UpdatesCheckOnStart,
            };

            var allow = Dialogs.Action(Loc.Get("upd.allow"), primary: true);
            allow.Click += async (_, _) =>
            {
                model.UpdatesCheckOnStart = onStart.IsChecked == true;
                model.UpdatesAllowed = true;
                await CheckAsync();
            };

            Fill(
                new TextBlock { Text = Loc.Get("upd.askTitle"), Classes = { "section" } },
                Dialogs.Body(Loc.Get("upd.askBody")),
                Bullets(Loc.Get("upd.askPoints")),
                new TextBlock
                {
                    Text = AppUpdate.LatestReleaseUrl,
                    Classes = { "mono", "muted" },
                    TextWrapping = TextWrapping.Wrap,
                },
                onStart,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { allow, PageLink() },
                });
        }

        /// <summary>Разрешение есть: можно проверять, а можно и отозвать.</summary>
        private void ShowAllowed()
        {
            var onStart = new CheckBox
            {
                Content = Loc.Get("upd.checkOnStart"),
                IsChecked = model.UpdatesCheckOnStart,
            };
            onStart.IsCheckedChanged += (_, _) => model.UpdatesCheckOnStart = onStart.IsChecked == true;

            var check = Dialogs.Action(Loc.Get("upd.check"), primary: true);
            check.Click += async (_, _) => await CheckAsync();

            var revoke = Dialogs.Action(Loc.Get("upd.revoke"));
            revoke.Click += (_, _) =>
            {
                model.UpdatesAllowed = false;
                model.UpdatesCheckOnStart = false;
                model.NoticeVersion(null);
                ShowPermissionRequest();
            };

            Fill(
                new TextBlock
                {
                    Text = Loc.Get("upd.allowed"),
                    Classes = { "secondary" },
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = model.LastUpdateCheckText, Classes = { "muted" } },
                onStart,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { check, revoke },
                });
        }

        private async Task CheckAsync()
        {
            Fill(new TextBlock { Text = Loc.Get("upd.checking"), Classes = { "body" } });

            var status = await model.Updates.CheckAsync(_cancel.Token);
            model.RememberUpdateCheck();

            if (_cancel.IsCancellationRequested) return;

            switch (status.State)
            {
                case UpdateState.Available when status.Release is not null:
                    model.NoticeVersion(status.Release.Version);
                    ShowAvailable(status.Release);
                    break;

                case UpdateState.UpToDate:
                    model.NoticeVersion(null);
                    ShowMessage(status.Message, "FreshBrush");
                    break;

                default:
                    ShowMessage(status.Message, "DangerBrush");
                    break;
            }
        }

        /// <summary>Сразу показать выпуск - вход для снимков экрана.</summary>
        public void ShowRelease(ReleaseInfo release)
        {
            window.Closed += (_, _) => _cancel.Cancel();
            ShowAvailable(release);
        }

        private void ShowAvailable(ReleaseInfo release)
        {
            var download = Dialogs.Action(Loc.Get("upd.download"), primary: true);
            download.Click += async (_, _) => await DownloadAsync(release);

            var skip = Dialogs.Action(Loc.Get("upd.skip"));
            skip.Click += (_, _) =>
            {
                model.SkippedVersion = release.Version;
                model.NoticeVersion(null);
                ShowMessage(Loc.Get("upd.skipped"), "TextSecondaryBrush");
            };

            var items = new List<Control>
            {
                new TextBlock
                {
                    Text = Loc.Get("upd.available", release.Version),
                    Classes = { "section" },
                    Foreground = Dialogs.Res<IBrush>("AccentBrightBrush"),
                },
            };

            if (release.Notes.Trim().Length > 0)
            {
                items.Add(new TextBlock { Text = Loc.Get("upd.whatsNew"), Classes = { "section" } });
                items.Add(new Border
                {
                    Classes = { "inset" },
                    Padding = new Thickness(12),
                    MaxHeight = 220,
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        // Текст выпуска приходит из сети, и это именно текст:
                        // он показывается как есть, без разметки и ссылок.
                        Content = new SelectableTextBlock
                        {
                            Text = release.Notes.Trim(),
                            Classes = { "secondary" },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                });
            }

            items.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { download, skip, PageLink(release.PageUrl) },
            });

            Fill([.. items]);
        }

        private async Task DownloadAsync(ReleaseInfo release)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
            var label = new TextBlock { Text = Loc.Get("upd.downloading", 0), Classes = { "body" } };

            Fill(label, bar);

            var progress = new Progress<double>(value =>
            {
                bar.Value = value * 100;
                label.Text = Loc.Get("upd.downloading", (int)(value * 100));
            });

            var result = await model.Updates.DownloadAsync(release, progress, _cancel.Token);
            if (_cancel.IsCancellationRequested) return;

            if (!result.Success || result.Folder is null)
            {
                ShowMessage(result.Message, "DangerBrush");
                return;
            }

            ShowReady(result.Folder, result.Message);
        }

        /// <summary>Скачано и проверено - остался только последний шаг, и его делает человек.</summary>
        private void ShowReady(string staging, string message)
        {
            var target = PortableSettings.AppFolder;

            if (!UpdateInstaller.CanInstallInto(target))
            {
                var open = Dialogs.Action(Loc.Get("upd.openFolder"));
                open.Click += (_, _) => ExternalLinks.OpenFolder(staging);

                Fill(
                    new TextBlock
                    {
                        Text = Loc.Get("upd.cannotInstall", staging),
                        Classes = { "body" },
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Dialogs.Res<IBrush>("WarnBrush"),
                    },
                    open);
                return;
            }

            var install = Dialogs.Action(Loc.Get("upd.install"), primary: true);
            install.Click += (_, _) =>
            {
                install.IsEnabled = false;
                if (!UpdateInstaller.Launch(staging, target, model.Log))
                {
                    install.IsEnabled = true;
                    ShowMessage(Loc.Get("upd.installFailedTitle"), "DangerBrush");
                    return;
                }

                // Дальше работает уже запущенный установщик: он ждёт, пока эта
                // копия закроется, и только тогда трогает файлы.
                shutdown();
            };

            Fill(
                new TextBlock
                {
                    Text = message,
                    Classes = { "section" },
                    Foreground = Dialogs.Res<IBrush>("FreshBrush"),
                },
                Dialogs.Body(Loc.Get("upd.readyBody")),
                install);
        }

        private void ShowMessage(string text, string brushKey) =>
            Fill(new TextBlock
            {
                Text = text,
                Classes = { "body" },
                TextWrapping = TextWrapping.Wrap,
                Foreground = Dialogs.Res<IBrush>(brushKey),
            });

        private void Fill(params Control[] children)
        {
            content.Children.Clear();
            foreach (var child in children) content.Children.Add(child);
        }

        private static Control PageLink(string? url = null)
        {
            var button = new Button
            {
                Content = Loc.Get("upd.openPage"),
                Classes = { "link" },
                VerticalAlignment = VerticalAlignment.Center,
            };

            var address = url is not null && AppUpdate.IsTrustedUrl(url) ? url : AppUpdate.ReleasesPageUrl;
            button.Click += (_, _) => ExternalLinks.Open(address);
            return button;
        }

        /// <summary>Перечень по строкам: каждая с точкой, чтобы читалось как список.</summary>
        private static Control Bullets(string text)
        {
            var panel = new StackPanel { Spacing = 6 };

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "·  " + line.Trim(),
                    Classes = { "secondary" },
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            return panel;
        }
    }
}
