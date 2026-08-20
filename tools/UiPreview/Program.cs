using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ErdtreeKeeper;
using ErdtreeKeeper.Core;
using ErdtreeKeeper.ViewModels;
using ErdtreeKeeper.Views;

// Рисует окна приложения в файлы, не открывая их на рабочем столе.
// Запуск: dotnet run --project tools/UiPreview -- <папка> [ширина] [высота]

var outputDir = args.Length > 0 ? args[0] : "ui-preview";
var width = args.Length > 1 && int.TryParse(args[1], out var w) ? w : 1180;
var height = args.Length > 2 && int.TryParse(args[2], out var h) ? h : 840;

// Четвёртый аргумент - язык интерфейса: снимки нужны на обоих, английский
// текст длиннее русского и разметку ломает первым. Модель окна ставит язык
// из настроек в своём конструкторе, поэтому запрошенный язык применяется
// после создания каждой модели - см. NewModel().
var wanted = args.Length > 3 && args[3].Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
Environment.SetEnvironmentVariable("ERDTREE_KEEPER_UI_LANG", wanted);

// Язык кладётся в настройки: модель окна читает его в конструкторе и от него
// же зависят имена папок по умолчанию. Иначе на английском снимке мелькала бы
// русская папка "Снимки".
var seed = PortableSettings.Load();
seed.Values.Language = wanted == "en" ? nameof(Lang.En) : nameof(Lang.Ru);
seed.Values.SnapshotFolder = null;
seed.Values.AutoSnapshotFolder = null;
seed.Values.LastSnapshotName = null;
seed.Save();

Directory.CreateDirectory(outputDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .Start((_, _) => Shoot(outputDir, width, height), args);

static void Shoot(string outputDir, int width, int height)
{
    // Первый запуск: с приветственным экраном.
    var firstModel = NewModel();
    UseFakeAccount(firstModel);
    var first = new MainWindow { DataContext = firstModel, Width = width, Height = height };
    Capture(first, "01-первый-запуск.png", outputDir);

    // Обычный вид.
    var model = NewModel();
    model.DismissOnboardingCommand.Execute(null);
    model.RefreshAccounts();
    UseFakeAccount(model);
    model.AnalyzeCommand.Execute(null);
    for (var i = 0; i < 30 && model.SaveContext is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }
    var main = new MainWindow { DataContext = model, Width = width, Height = height };
    Capture(main, "02-главный-экран.png", outputDir);

    // Карточка игрока - на настоящем сейве.
    if (model.SaveContext is { } ctx)
    {
        var c = ctx.Character;
        Console.WriteLine($"   {c.Name}: ур.{c.Level} (из характеристик {c.LevelFromStats}), "
                          + $"HP {c.MaxHp}, FP {c.MaxFp}, вын. {c.MaxStamina}, "
                          + $"рун {c.Runes}, в игре {c.PlayedText}");
        Capture(Dialogs.CreatePlayerCardWindow(ctx), "10-карточка-игрока.png", outputDir);
    }
    else
    {
        Console.WriteLine("   сейв не прочитан - карточка не снята");
    }

    // Диалоги. Пути настоящие - в них видно, что ничего не зашито жёстко.
    Capture(
        Dialogs.CreateTransparencyWindow(model.SettingsPath, model.SnapshotFolder),
        "04-что-программа-делает.png", outputDir);

    // Список переключён на автосохранения, настройки раскрыты.
    var auto = NewModel();
    auto.DismissOnboardingCommand.Execute(null);
    auto.RefreshAccounts();
    UseFakeAccount(auto);
    auto.AutoSnapshotEnabled = true;
    // Длинное имя: на нём строка предпросмотра переносилась и выталкивала
    // папку автосохранений за нижний край.
    auto.SnapshotName = Loc.Current.IsEnglish
        ? "DLC_#3 Revered Spirit Ash - Abandoned Ailing Village_after"
        : "DLC_#3 Прах славного духа - Abandoned Ailing Village_after";
    auto.SnapshotSourceIndex = 1;
    Console.WriteLine($"   интервал: {auto.AutoMinutes} мин, хранить: {auto.AutoKeep}");
    Console.WriteLine($"   папка автосохранений: {auto.ListFolder}");
    Console.WriteLine($"   найдено файлов: {auto.Snapshots.Count}");
    var autoWindow = new MainWindow { DataContext = auto, Width = width, Height = height };
    // Выбор теперь принадлежит модели - задаём его прямо, без обращения к списку.
    foreach (var row in auto.Snapshots.Take(2)) row.IsSelected = true;
    Console.WriteLine($"   {auto.SelectionSummary}; кнопка: {auto.DeleteLabel}");
    Capture(autoWindow, "07-автосохранения.png", outputDir);

    // Сортировка по имени: тот же список в другом порядке.
    auto.SortBy(SnapshotSort.Name);
    Console.WriteLine($"   {auto.NameSortLabel}: {string.Join(" | ", auto.Snapshots.Select(s => s.Name[..Math.Min(18, s.Name.Length)]))}");
    Capture(
        new MainWindow { DataContext = auto, Width = width, Height = height },
        "08-сортировка-по-имени.png", outputDir);

    // Журнал открыт: левая колонка не должна от этого сжиматься.
    var withLog = NewModel();
    withLog.DismissOnboardingCommand.Execute(null);
    withLog.RefreshAccounts();
    UseFakeAccount(withLog);
    var logWindow = new MainWindow { DataContext = withLog, Width = width, Height = height };
    logWindow.Opened += (_, _) =>
    {
        var toggle = logWindow.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LogToggle");
        if (toggle is not null) toggle.IsChecked = true;
    };
    Capture(logWindow, "03-журнал-открыт.png", outputDir);

    Capture(
        Dialogs.CreateAutoSaveWindow(auto.AutoMinutes, auto.AutoKeep, auto.AutoFolder,
            _ => { }, _ => { }, () => Task.FromResult<string?>(null)),
        "09-автосохранение-настройки.png", outputDir);

    Capture(
        Dialogs.CreateAboutWindow(model.SettingsPath, model.IsPortable, model.SettingsFileState),
        "05-о-программе.png", outputDir);

    Capture(
        Dialogs.CreateReportWindow(Loc.Get("dlg.integrityTitle"), SampleReport()),
        "05-проверка-целостности.png", outputDir);

    Capture(
        Dialogs.CreateReportWindow("Пример длинной строки", string.Join("\n", Enumerable.Repeat(
            "Очень длинная строка, которая заведомо шире окна и должна прокручиваться, а не обрезаться молча.", 3))),
        "06-длинный-текст.png", outputDir);

    Console.WriteLine($"Готово: {Path.GetFullPath(outputDir)}");
}


// Настоящий SteamID64 резолвится в публичный профиль, поэтому на снимках
// экрана стоит вымышленный: он ниже диапазона существующих аккаунтов.
/// <summary>Модель окна на запрошенном языке, а не на том, что в настройках.</summary>
static MainViewModel NewModel()
{
    var model = new MainViewModel();
    Loc.Current.Language =
        Environment.GetEnvironmentVariable("ERDTREE_KEEPER_UI_LANG") == "en" ? Lang.En : Lang.Ru;

    // Первые записи журнала сделаны в конструкторе, на языке из настроек.
    // В работе так и надо, а для снимка экрана они бы смешали два языка.
    model.Log.Entries.Clear();
    return model;
}

static void UseFakeAccount(MainViewModel model)
{
    var root = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_FAKE_SAVES");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

    // Окно при открытии перечитывает аккаунты само, поэтому подменяется не
    // список, а папка, в которой он ищется: внутри лежит вымышленный
    // SteamID64 - настоящий номер из снимков экрана резолвится в живой профиль.
    model.SavesRoot = root;
    model.RefreshAccounts();
}

// Образец отчёта собирается из тех же строк, что и настоящий, - иначе на
// английском снимке экрана оказался бы русский текст.
static string SampleReport() => string.Join(Environment.NewLine,
[
    Loc.Get("report.file", "ER0000.sl2"),
    "",
    Loc.Get("report.sizeNormal", "28 967 888"),
    "",
    Loc.Get("report.howItWorks1"),
    Loc.Get("report.howItWorks2"),
    "",
    $"  {Loc.Get("block.slot", 1),-10}  " + Loc.Get("report.blockOk"),
    $"  {Loc.Get("block.slot", 2),-10}  " + Loc.Get("report.blockOk"),
    $"  {Loc.Get("block.slot", 3),-10}  " + Loc.Get("report.blockBad", "5b8259aa", "8354dcaa"),
    $"  {Loc.Get("block.profile"),-10}  " + Loc.Get("report.blockOk"),
    "",
    Loc.Get("report.verdictBad", 1),
    Loc.Get("report.verdictBad2"),
]);

static void Capture(Window window, string name, string outputDir, Action<Window>? afterShow = null)
{
    window.Show();
    afterShow?.Invoke(window);

    // Даём разметке и привязкам отработать перед снимком.
    for (var i = 0; i < 8; i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(60);
    }

    Dispatcher.UIThread.RunJobs();

    using var frame = window.CaptureRenderedFrame();
    if (frame is null)
    {
        Console.WriteLine($"{name}: кадр не получен");
        return;
    }

    var path = Path.Combine(outputDir, name);
#pragma warning disable CS0618 // служебный инструмент, достаточно простого Save
    frame.Save(path);
#pragma warning restore CS0618
    Console.WriteLine($"{name}: {frame.PixelSize.Width}x{frame.PixelSize.Height}");

    window.Close();
}
