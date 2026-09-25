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
seed.Values.Games.Clear();
seed.Values.CustomGames.Clear();
seed.Values.SelectedGameId = GameProfiles.EldenRingId;

// Разрешение на сеть сбрасывается нарочно: ниже проверяется, что без него
// программа не делает ни одного запроса, и оставшееся с прошлого запуска
// "разрешено" эту проверку обессмыслило бы.
seed.Values.UpdatesAllowed = null;
seed.Values.UpdatesCheckOnStart = false;
seed.Save();

// Анонимизация сейва для снимков экрана: ERDTREE_KEEPER_ANONYMIZE="<откуда>|<куда>".
// Имена персонажей становятся "Tarnished", SteamID - вымышленным, контрольные
// суммы пересчитываются. Окно не нужно.
if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_ANONYMIZE") is { Length: > 0 } anonymize)
{
    var parts = anonymize.Split('|');
    Anonymize(parts[0], parts[1]);
    return;
}

// Разведка по играм: ERDTREE_KEEPER_GAMES=1 печатает, где каждая встроенная
// игра ищет сохранения на этой машине и что нашла. Окно не нужно.
if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_GAMES") == "1")
{
    foreach (var game in GameProfiles.BuiltIn)
    {
        var root = game.ResolveRoot();
        var accounts = GameSaves.FindAccounts(game, root);
        var files = accounts.Sum(a => GameSaves.FindSaveFiles(game, a.Path).Count);

        Console.WriteLine($"{game.Name}");
        foreach (var candidate in game.ResolveRoots())
        {
            Console.WriteLine($"    {(Directory.Exists(candidate) ? "есть " : "нет  ")} {candidate}");
        }

        Console.WriteLine($"    -> аккаунтов {accounts.Count}, файлов {files}");
        foreach (var account in accounts)
        {
            foreach (var file in GameSaves.FindSaveFiles(game, account.Path))
            {
                var bytes = File.ReadAllBytes(file.Path);
                var check = SaveIntegrity.Inspect(game, bytes);
                var verdict = check.Ok ? "годен" : "ОТКАЗ: " + check.Problem;
                var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(4, bytes.Length));
                Console.WriteLine($"       {account.SteamId}  {file.Name}  {file.Length / 1024} КБ  [{magic}]  {verdict}");
            }
        }
    }

    return;
}

// Настоящая проверка обновления: ERDTREE_KEEPER_NETTEST=check спрашивает
// GitHub, =full ещё и качает архив со сверкой контрольной суммы. Окно не
// нужно - выходим до старта Avalonia.
if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_NETTEST") is { Length: > 0 } netMode)
{
    await NetTest(netMode == "full");
    return;
}

// Замеры вместо снимков: ERDTREE_KEEPER_BENCH=<путь к .sl2>. Окно не нужно,
// поэтому выходим до старта Avalonia.
if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_BENCH") is { Length: > 0 } benchSave && File.Exists(benchSave))
{
    await Bench(benchSave);
    return;
}

Directory.CreateDirectory(outputDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .Start((_, _) => Shoot(outputDir, width, height), args);

/// <summary>
/// Копия сейва, которую можно показывать публично.
///
/// Настоящее имя персонажа и SteamID64 на снимке экрана ведут к живому
/// профилю Steam. Здесь они заменяются: имена - на "Tarnished", номер - на
/// вымышленный, ниже диапазона существующих аккаунтов. Имя лежит в поле на 16
/// символов, поэтому замена пишется в поле целиком и только там, где за
/// именем действительно пусто, - иначе это было бы не поле, а совпадение
/// внутри других данных. После замены пересчитываются все 11 контрольных сумм:
/// без этого программа честно назвала бы файл повреждённым.
/// </summary>
static void Anonymize(string source, string target)
{
    const string fakeName = "Tarnished";
    const ulong fakeSteamId = 76561190000000001UL;
    const int nameField = 32;

    var data = File.ReadAllBytes(source);
    var slots = SaveParser.ReadSlots(data);

    var names = slots.Select(slot => slot.Name).Where(n => n.Length > 0).Distinct().ToList();
    var ids = slots
        .Select(slot => SaveParser.ReadSlotDetails(data, slot.Index)?.SteamId)
        .Where(id => ulong.TryParse(id, out _))
        .Select(id => ulong.Parse(id!))
        .Distinct()
        .ToList();

    var replacement = new byte[nameField];
    System.Text.Encoding.Unicode.GetBytes(fakeName).CopyTo(replacement, 0);

    var renamed = 0;
    var skipped = 0;
    foreach (var name in names)
    {
        var needle = System.Text.Encoding.Unicode.GetBytes(name);
        if (needle.Length > nameField) { skipped++; continue; }

        var from = 0;
        while (true)
        {
            var at = data.AsSpan(from).IndexOf(needle);
            if (at < 0) break;
            at += from;
            from = at + needle.Length;

            var tail = data.AsSpan(at + needle.Length, nameField - needle.Length);
            if (tail.ContainsAnyExcept((byte)0)) { skipped++; continue; }

            replacement.CopyTo(data, at);
            renamed++;
        }
    }

    var fakeBytes = BitConverter.GetBytes(fakeSteamId);
    var reids = 0;
    foreach (var id in ids)
    {
        var needle = BitConverter.GetBytes(id);
        var from = 0;
        while (true)
        {
            var at = data.AsSpan(from).IndexOf(needle);
            if (at < 0) break;
            at += from;
            fakeBytes.CopyTo(data, at);
            from = at + needle.Length;
            reids++;
        }
    }

    for (var i = 0; i < Sl2File.SlotCount; i++)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            data.AsSpan((int)Sl2File.SlotDataOffset(i), Sl2File.SlotDataSize));
        hash.CopyTo(data, (int)Sl2File.SlotChecksumOffset(i));
    }

    var profileHash = System.Security.Cryptography.MD5.HashData(
        data.AsSpan((int)(Sl2File.ProfileOffset + Sl2File.ChecksumSize), Sl2File.ProfileDataSize));
    profileHash.CopyTo(data, (int)Sl2File.ProfileOffset);

    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, data);

    // Проверка результата, а не вера в него.
    var leftNames = names.Where(n =>
        data.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(n)) >= 0).ToList();
    var leftIds = ids.Where(id => data.AsSpan().IndexOf(BitConverter.GetBytes(id)) >= 0).ToList();
    var integrity = Sl2File.CheckIntegrity(data);
    var after = SaveParser.ReadSlots(data).Select(slot => slot.Name).Distinct();

    // Главное - то, что программа покажет на экране: персонаж, которого она
    // выбирает сама, и номер аккаунта. Короткие имена других слотов вроде
    // "42" по байтам не отличить от случайных данных, и заменять их вслепую
    // значило бы портить сейв; на экран они не попадают.
    var shown = SaveContextReader.Read(data);

    Console.WriteLine($"имён персонажей: {names.Count}, замен: {renamed}, пропущено совпадений: {skipped}");
    Console.WriteLine($"SteamID: {ids.Count}, замен: {reids}, осталось: {leftIds.Count}");
    Console.WriteLine($"имена в слотах после замены: {string.Join(", ", after)}");
    Console.WriteLine($"на экране: {shown?.Character.Name}, аккаунт {shown?.SteamId}");
    Console.WriteLine($"целостность: {(integrity.AllOk ? "все 11 сумм сошлись" : integrity.Problem)}");
    Console.WriteLine(shown?.Character.Name == fakeName && leftIds.Count == 0 && integrity.AllOk
        ? "АНОНИМИЗАЦИЯ ПРОЙДЕНА: на экране вымышленные имя и номер"
        : "АНОНИМИЗАЦИЯ НЕ ПРОЙДЕНА");
    _ = leftNames;
}

/// <summary>
/// Снимки для карточки на сайте: главное окно, другая игра, обновление и
/// проверка целостности. Всё на вымышленных данных из фикстуры - настоящий
/// SteamID и имя персонажа на публичной странице ни к чему.
///
/// Раскладка фикстуры: er\&lt;SteamID&gt;\ER0000.sl2 (после анонимизации),
/// ds3\&lt;id&gt;\DS30000.sl2, notes-ru.md и notes-en.md - заметки выпуска.
/// </summary>
static void ShootShowcase(string fixture, string outputDir, bool english)
{
    var suffix = english ? "en" : "ru";
    var width = 1180;
    // Выше обычного: карточка выбора игры сдвинула колонку вниз, и при 840
    // кнопка "Сделать снимок" уходила под край.
    var height = 980;

    // Папка снимков видна на снимке экрана целиком. Внутри фикстуры путь
    // выдаёт имя пользователя Windows и служебные папки, поэтому для съёмки
    // можно указать нейтральный корень - так путь выглядит как у игрока.
    var snapshotsRoot = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_SHOWCASE_SNAPSHOTS")
                        ?? Path.Combine(fixture, "snapshots");
    var localFolder = Path.Combine(snapshotsRoot, english ? "Snapshots" : "Снимки");

    // Список снимков собирается из копий фикстуры под именами из справочника
    // игры: так названия мест настоящие и на нужном языке.
    string Boss(string ru, string en)
    {
        var point = MapPoints.Bosses.FirstOrDefault(b => b.Name.Contains(en, StringComparison.OrdinalIgnoreCase));
        if (point is null) return english ? en : ru;
        return english || string.IsNullOrWhiteSpace(point.Ru) ? point.Name : point.Ru;
    }

    // Имя собирается так же, как кнопками в окне: метка DLC первой, место,
    // затем суффикс пары - и всё через санитизацию имени файла.
    static string Named(string place, string suffix, bool dlc)
    {
        var name = dlc ? SnapshotNaming.EnsureDlcTag("") : "";
        name = SnapshotNaming.Append(name, place);
        return SnapshotNaming.ToFileName(SnapshotNaming.WithPairSuffix(name, suffix));
    }

    var erSave = Directory.GetFiles(Path.Combine(fixture, "er"), "ER0000.sl2", SearchOption.AllDirectories)[0];
    var erSnapshots = localFolder;
    if (Directory.Exists(erSnapshots)) Directory.Delete(erSnapshots, recursive: true);
    Directory.CreateDirectory(erSnapshots);

    var erNames = new[]
    {
        (Named(Boss("Маргит", "Margit"), SnapshotNaming.BeforeSuffix, dlc: false), -9),
        (Named(Boss("Реннала", "Rennala"), SnapshotNaming.AfterSuffix, dlc: false), -6),
        (Named(Boss("Радан", "Radahn"), SnapshotNaming.BeforeSuffix, dlc: false), -4),
        (Named(Boss("Мессмер", "Messmer"), SnapshotNaming.BeforeSuffix, dlc: true), -1),
    };

    foreach (var (name, days) in erNames)
    {
        var path = Path.Combine(erSnapshots, name);
        File.Copy(erSave, path, overwrite: true);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(days).AddHours(-3));
    }

    // ─── 1. Главное окно Elden Ring ─────────────────────────────────────
    var main = NewModel();
    main.DismissOnboardingCommand.Execute(null);
    main.SelectedGame = main.Games.First(g => g.Id == GameProfiles.EldenRingId);
    main.SavesRoot = Path.Combine(fixture, "er");
    main.SnapshotFolder = erSnapshots;
    main.RefreshAccounts();
    main.AnalyzeCommand.Execute(null);
    for (var i = 0; i < 40 && main.SaveContext is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }

    main.AutoSnapshotEnabled = true;
    main.SnapshotName = "";
    main.AddLocationCommand.Execute(null);
    for (var i = 0; i < 20 && main.SnapshotName.Length == 0; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }
    main.AddBeforeCommand.Execute(null);
    main.RefreshSnapshots();
    main.Log.Entries.Clear();

    Console.WriteLine($"   персонаж: {main.SaveContext?.Character.Name}, место: {main.SaveContext?.Location?.Display}");
    Console.WriteLine($"   имя снимка: {main.SnapshotName}, снимков в списке: {main.Snapshots.Count}");
    Capture(new MainWindow { DataContext = main, Width = width, Height = height },
        $"keeper-main-{suffix}.png", outputDir);

    // ─── 2. Dark Souls III ──────────────────────────────────────────────
    var ds3Snapshots = Path.Combine(localFolder, "Dark Souls III");
    if (Directory.Exists(ds3Snapshots)) Directory.Delete(ds3Snapshots, recursive: true);
    Directory.CreateDirectory(ds3Snapshots);

    var ds3Save = Directory.GetFiles(Path.Combine(fixture, "ds3"), "DS30000.sl2", SearchOption.AllDirectories)[0];
    var ds3Names = new[]
    {
        (english ? "Nameless King_before" : "Безымянный король_before", -5),
        (english ? "Twin Princes_after" : "Принцы-близнецы_after", -2),
        (SnapshotNaming.AppendTime(english ? "Ringed City" : "Город за стеной", DateTime.Now.AddHours(-20)), 0),
    };

    foreach (var (name, days) in ds3Names)
    {
        var path = Path.Combine(ds3Snapshots, name + ".sl2");
        File.Copy(ds3Save, path, overwrite: true);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(days).AddHours(-1));
    }

    var ds3 = NewModel();
    ds3.DismissOnboardingCommand.Execute(null);
    ds3.SelectedGame = ds3.Games.First(g => g.Id == "dark-souls-3");
    ds3.SavesRoot = Path.Combine(fixture, "ds3");
    ds3.SnapshotFolder = ds3Snapshots;
    ds3.RefreshAccounts();
    ds3.SnapshotName = english ? "Soul of Cinder" : "Душа пепла";
    ds3.AddTimeCommand.Execute(null);
    ds3.RefreshSnapshots();
    ds3.Log.Entries.Clear();

    Console.WriteLine($"   игра: {ds3.Game.Name}, файлов: {ds3.SaveFiles.Count}, снимков: {ds3.Snapshots.Count}");
    Capture(new MainWindow { DataContext = ds3, Width = width, Height = height },
        $"keeper-games-{suffix}.png", outputDir);

    // ─── 3. Обновление: переход с предыдущего патча на текущую версию ───
    // Выпуск настоящий - номер этой сборки и её раздел журнала изменений,
    // установленной считается предыдущая версия: ровно то, что увидит игрок.
    var current = AppInfo.Version;
    var parts = current.Split('.').Select(int.Parse).ToArray();
    var previous = parts[2] > 0 ? $"{parts[0]}.{parts[1]}.{parts[2] - 1}" : current;

    var notes = File.ReadAllText(Path.Combine(fixture, $"notes-{suffix}.md"));
    var release = new ReleaseInfo(
        "v" + current,
        current,
        AppUpdate.ReadableNotes(notes),
        $"https://github.com/valiDol42/erdtree-keeper/releases/tag/v{current}",
        [],
        DateTimeOffset.Now);

    var updates = NewModel();
    Capture(UpdateDialog.CreatePreview(updates, release, previous),
        $"keeper-update-{suffix}.png", outputDir);

    // ─── 4. Проверка целостности - настоящий отчёт по фикстуре ──────────
    var check = SaveIntegrity.Inspect(GameProfiles.EldenRing, File.ReadAllBytes(erSave));
    Capture(Dialogs.CreateReportWindow(Loc.Get("dlg.integrityTitle"),
            SaveIntegrity.BuildReport(GameProfiles.EldenRing, check, "ER0000.sl2")),
        $"keeper-integrity-{suffix}.png", outputDir);

    // Настройки стенда общие: возвращаем Elden Ring, чтобы следующий
    // обычный запуск начинался с того же, с чего всегда.
    main.SelectedGame = main.Games.First(g => g.Id == GameProfiles.EldenRingId);
}

/// <summary>
/// Кадры для руководства Steam.
///
/// Стенд для этого запускается не из папки сборки, а из копии, положенной
/// туда, куда программу распаковал бы игрок: тогда пути в окнах "О программе"
/// и "Что программа делает" настоящие - папка программы, файл настроек рядом
/// с ней, "Снимки" внутри, - а не служебные папки разработки.
///
/// Сохранения берутся из анонимизированной фикстуры: персонаж "Tarnished",
/// номер аккаунта вымышленный. Порядок кадров важен: приветствие снимается
/// первым, пока его не закрыли, а переключение языка - последним, потому что
/// настройки у всех моделей общие.
/// </summary>
static void ShootSteam(string fixture, string outputDir, bool english)
{
    var width = 1180;
    var height = 980;
    var root = PortableSettings.AppFolder;

    // Сохранения - копией фикстуры рядом с программой: чтение и запись в
    // журнале операций указывают на эти пути.
    var saves = Path.Combine(root, "saves");
    if (Directory.Exists(saves)) Directory.Delete(saves, recursive: true);
    CopyTree(Path.Combine(fixture, "er"), Path.Combine(saves, "EldenRing"));
    CopyTree(Path.Combine(fixture, "ds3"), Path.Combine(saves, "DarkSoulsIII"));
    var erRoot = Path.Combine(saves, "EldenRing");
    var erSave = Directory.GetFiles(erRoot, "ER0000.sl2", SearchOption.AllDirectories)[0];

    foreach (var old in new[] { "Снимки", "Snapshots" })
    {
        var folder = Path.Combine(root, old);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    // Приветствие видно только при первом запуске - поэтому настройки с
    // чистого листа и этот кадр первым.
    // Язык записан сразу - как у игрока, чья система на этом языке. Без него
    // первый запуск взял бы язык этой машины, и английский набор получил бы
    // закреплённую папку "Снимки".
    var settingsFile = Path.Combine(root, "erdtree-keeper.settings.json");
    File.WriteAllText(settingsFile, "{\"Language\":\"" + (english ? "En" : "Ru") + "\"}");

    MainViewModel Fresh()
    {
        var model = NewModel();
        model.SavesRoot = erRoot;
        model.RefreshAccounts();
        return model;
    }

    void WaitFor(Func<bool> done, int tries = 40)
    {
        for (var i = 0; i < tries && !done(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }
    }

    // ─── 02. Первый запуск ──────────────────────────────────────────────
    var first = Fresh();
    Capture(new MainWindow { DataContext = first, Width = width, Height = height }, "02-first-run.png", outputDir);
    first.DismissOnboardingCommand.Execute(null);

    // Снимки в списке: четыре ручных и три автоснимка, под именами из
    // справочника игры на нужном языке.
    var snapshots = first.SnapshotFolder;
    Directory.CreateDirectory(snapshots);

    string Boss(string ru, string en)
    {
        var point = MapPoints.Bosses.FirstOrDefault(b => b.Name.Contains(en, StringComparison.OrdinalIgnoreCase));
        if (point is null) return english ? en : ru;
        return english || string.IsNullOrWhiteSpace(point.Ru) ? point.Name : point.Ru;
    }

    static string Named(string place, string suffix, bool dlc)
    {
        var name = dlc ? SnapshotNaming.EnsureDlcTag("") : "";
        name = SnapshotNaming.Append(name, place);
        return SnapshotNaming.ToFileName(SnapshotNaming.WithPairSuffix(name, suffix));
    }

    foreach (var (name, days) in new[]
             {
                 (Named(Boss("Маргит", "Margit"), SnapshotNaming.BeforeSuffix, false), -9),
                 (Named(Boss("Реннала", "Rennala"), SnapshotNaming.AfterSuffix, false), -6),
                 (Named(Boss("Радан", "Radahn"), SnapshotNaming.BeforeSuffix, false), -4),
                 (Named(Boss("Мессмер", "Messmer"), SnapshotNaming.BeforeSuffix, true), -1),
             })
    {
        var path = Path.Combine(snapshots, name);
        File.Copy(erSave, path, overwrite: true);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(days).AddHours(-3));
    }

    var context = SaveContextReader.Read(File.ReadAllBytes(erSave));
    var autoFolder = Path.Combine(snapshots, SnapshotService.AutoFolder);
    Directory.CreateDirectory(autoFolder);
    foreach (var minutes in new[] { 95, 60, 25 })
    {
        var at = DateTime.Now.AddMinutes(-minutes);
        var path = Path.Combine(autoFolder, SnapshotNaming.AutoName(context, at));
        File.Copy(erSave, path, overwrite: true);
        File.SetLastWriteTime(path, at);
    }

    // ─── 01. Главное окно ───────────────────────────────────────────────
    var main = Fresh();
    main.AnalyzeCommand.Execute(null);
    WaitFor(() => main.SaveContext is not null);
    main.AutoSnapshotEnabled = true;
    main.SnapshotName = "";
    main.AddLocationCommand.Execute(null);
    WaitFor(() => main.SnapshotName.Length > 0, 20);
    main.AddBeforeCommand.Execute(null);
    main.RefreshSnapshots();
    main.Log.Entries.Clear();
    Console.WriteLine($"   персонаж: {main.SaveContext?.Character.Name}, снимков: {main.Snapshots.Count}");
    Capture(new MainWindow { DataContext = main, Width = width, Height = height }, "01-main.png", outputDir);

    // ─── 03. Карточка персонажа, 04. Проверка целостности ───────────────
    if (main.SaveContext is { } ctx)
        Capture(Dialogs.CreatePlayerCardWindow(ctx), "03-player-card.png", outputDir);

    var check = SaveIntegrity.Inspect(GameProfiles.EldenRing, File.ReadAllBytes(erSave));
    Capture(Dialogs.CreateReportWindow(Loc.Get("dlg.integrityTitle"),
        SaveIntegrity.BuildReport(GameProfiles.EldenRing, check, "ER0000.sl2")), "04-integrity.png", outputDir);

    // ─── 05. Настройки автосохранения, 06. Список автосохранений ────────
    Capture(Dialogs.CreateAutoSaveWindow(main.AutoMinutes, main.AutoKeep, main.AutoFolder,
        _ => { }, _ => { }, () => Task.FromResult<string?>(null)), "05-autosave-settings.png", outputDir);

    var autoList = Fresh();
    autoList.AutoSnapshotEnabled = true;
    autoList.SnapshotSourceIndex = 1;
    autoList.Log.Entries.Clear();
    Console.WriteLine($"   автосохранений в списке: {autoList.Snapshots.Count}");
    Capture(new MainWindow { DataContext = autoList, Width = width, Height = height }, "06-autosave-list.png", outputDir);

    // ─── 07. Что программа делает, 08. О программе ──────────────────────
    // Папка сохранений - та, что программа показала бы игроку: настоящий
    // путь игры на этой машине, а не копия фикстуры.
    Capture(Dialogs.CreateTransparencyWindow(main.SettingsPath, main.SnapshotFolder,
        GameProfiles.EldenRing.ResolveRoot()), "07-what-it-does.png", outputDir);
    Capture(Dialogs.CreateAboutWindow(main.SettingsPath, main.IsPortable, main.SettingsFileState),
        "08-about.png", outputDir);

    // ─── 09. Журнал операций: настоящий снимок, сделанный кнопкой ───────
    var logged = Fresh();
    logged.Log.Entries.Clear();
    logged.AnalyzeCommand.Execute(null);
    WaitFor(() => logged.SaveContext is not null);
    logged.SnapshotName = "";
    logged.AddBossCommand.Execute(null);
    WaitFor(() => logged.SnapshotName.Length > 0, 20);
    logged.CreateSnapshotCommand.Execute(null);
    WaitFor(() => logged.Log.Entries.Any(e => e.Kind == ActivityKind.Write));
    var logWindow = new MainWindow { DataContext = logged, Width = width, Height = height };
    logWindow.Opened += (_, _) =>
    {
        var toggle = logWindow.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LogToggle");
        if (toggle is not null) toggle.IsChecked = true;
    };
    Console.WriteLine($"   записей в журнале: {logged.Log.Entries.Count}");
    Capture(logWindow, "09-activity-log.png", outputDir);

    // ─── 11. Dark Souls III ─────────────────────────────────────────────
    var ds3 = NewModel();
    ds3.SelectedGame = ds3.Games.First(g => g.Id == "dark-souls-3");
    ds3.SavesRoot = Path.Combine(saves, "DarkSoulsIII");
    ds3.RefreshAccounts();
    var ds3Snapshots = ds3.SnapshotFolder;
    Directory.CreateDirectory(ds3Snapshots);
    var ds3Save = Directory.GetFiles(ds3.SavesRoot, "DS30000.sl2", SearchOption.AllDirectories)[0];
    foreach (var (name, days) in new[]
             {
                 (english ? "Nameless King_before" : "Безымянный король_before", -5),
                 (english ? "Twin Princes_after" : "Принцы-близнецы_after", -2),
                 (SnapshotNaming.AppendTime(english ? "Ringed City" : "Город за стеной", DateTime.Now.AddHours(-20)), 0),
             })
    {
        var path = Path.Combine(ds3Snapshots, name + ".sl2");
        File.Copy(ds3Save, path, overwrite: true);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(days).AddHours(-1));
    }

    ds3.SnapshotName = english ? "Soul of Cinder" : "Душа пепла";
    ds3.AddTimeCommand.Execute(null);
    ds3.RefreshSnapshots();
    ds3.Log.Entries.Clear();
    Capture(new MainWindow { DataContext = ds3, Width = width, Height = height }, "11-games.png", outputDir);
    ds3.SelectedGame = ds3.Games.First(g => g.Id == GameProfiles.EldenRingId);

    // ─── 12. Добавление игры, 13. Обновление ────────────────────────────
    Capture(AddGameDialog.Create((_, _) => Task.FromResult<string?>(null), _ => { }), "12-add-game.png", outputDir);

    var current = AppInfo.Version;
    var parts = current.Split('.').Select(int.Parse).ToArray();
    var previous = parts[2] > 0 ? $"{parts[0]}.{parts[1]}.{parts[2] - 1}" : current;
    var notes = File.ReadAllText(Path.Combine(fixture, $"notes-{(english ? "en" : "ru")}.md"));
    var release = new ReleaseInfo("v" + current, current, AppUpdate.ReadableNotes(notes),
        $"https://github.com/valiDol42/erdtree-keeper/releases/tag/v{current}", [], DateTimeOffset.Now);
    Capture(UpdateDialog.CreatePreview(NewModel(), release, previous), "13-update.png", outputDir);

    // ─── 10. Переключение языка - последним ─────────────────────────────
    // Кадр показывает окно на другом языке: в русском руководстве -
    // английское, и наоборот. Язык потом возвращается в настройках.
    var switched = Fresh();
    switched.AnalyzeCommand.Execute(null);
    WaitFor(() => switched.SaveContext is not null);
    if (english) switched.IsRussian = true; else switched.IsEnglish = true;
    switched.Log.Entries.Clear();
    Capture(new MainWindow { DataContext = switched, Width = width, Height = height }, "10-language-switch.png", outputDir);
    if (english) switched.IsEnglish = true; else switched.IsRussian = true;
}

static void CopyTree(string from, string to)
{
    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(to, Path.GetRelativePath(from, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

/// <summary>
/// Проверка установки: обновление обязано заменить свои файлы и не тронуть
/// чужие. Настройки и снимки лежат в той же папке, и потерять их при
/// обновлении - худшее, что программа может сделать с человеком, который ей
/// как раз и доверил хранение сейвов.
/// </summary>
static void InstallTest()
{
    var root = Path.Combine(Path.GetTempPath(), "erdtree-keeper-installtest", Guid.NewGuid().ToString("N"));
    var target = Path.Combine(root, "программа");
    var staging = Path.Combine(root, "новое");
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(staging);

    try
    {
        // Так выглядит папка работающей программы: сама программа, библиотеки,
        // настройки и снимки рядом.
        File.WriteAllText(Path.Combine(target, "ErdtreeKeeper.exe"), "старая программа");
        File.WriteAllText(Path.Combine(target, "libSkiaSharp.dll"), "старая библиотека");
        File.WriteAllText(Path.Combine(target, "erdtree-keeper.settings.json"), "{\"мои\": \"настройки\"}");
        Directory.CreateDirectory(Path.Combine(target, "Снимки"));
        File.WriteAllText(Path.Combine(target, "Снимки", "Годрик.sl2"), "мой снимок");

        // А так - распакованный архив нового выпуска.
        File.WriteAllText(Path.Combine(staging, "ErdtreeKeeper.exe"), "новая программа");
        File.WriteAllText(Path.Combine(staging, "libSkiaSharp.dll"), "новая библиотека");
        File.WriteAllText(Path.Combine(staging, "README.md"), "новое описание");

        ErdtreeKeeper.Updates.UpdateInstaller.TryRunAsInstaller([
            "--apply-update",
            "--from", staging,
            "--to", target,
            ErdtreeKeeper.Updates.UpdateInstaller.NoLaunchSwitch,
        ]);

        var problem = ErdtreeKeeper.Updates.UpdateInstaller.LastProblem;
        var programUpdated = File.ReadAllText(Path.Combine(target, "ErdtreeKeeper.exe")) == "новая программа";
        var libraryUpdated = File.ReadAllText(Path.Combine(target, "libSkiaSharp.dll")) == "новая библиотека";
        var readmeAdded = File.Exists(Path.Combine(target, "README.md"));
        var settingsKept = File.ReadAllText(Path.Combine(target, "erdtree-keeper.settings.json")).Contains("настройки");
        var snapshotKept = File.ReadAllText(Path.Combine(target, "Снимки", "Годрик.sl2")) == "мой снимок";

        Console.WriteLine($"   помеха: {problem ?? "нет"}");
        Console.WriteLine($"   программа заменена: {programUpdated}");
        Console.WriteLine($"   библиотека заменена: {libraryUpdated}");
        Console.WriteLine($"   новые файлы добавлены: {readmeAdded}");
        Console.WriteLine($"   настройки целы: {settingsKept}");
        Console.WriteLine($"   снимок цел: {snapshotKept}");

        Console.WriteLine(problem is null && programUpdated && libraryUpdated
                          && readmeAdded && settingsKept && snapshotKept
            ? "   ПРОВЕРКА УСТАНОВКИ ПРОЙДЕНА: своё заменено, чужое не тронуто"
            : "   ПРОВЕРКА УСТАНОВКИ НЕ ПРОЙДЕНА");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}

/// <summary>
/// Проверка обновления на живом GitHub: то, что нельзя доказать тестом без
/// сети. Скачанный архив сверяется с опубликованной суммой - ради этого всё
/// и затевалось.
/// </summary>
static async Task NetTest(bool full)
{
    var log = new ActivityLog();
    var updates = new ErdtreeKeeper.Updates.UpdateService(log);

    Console.WriteLine($"установлена версия: {AppInfo.Version}");
    Console.WriteLine($"адрес запроса: {AppUpdate.LatestReleaseUrl}");

    var status = await updates.CheckAsync();
    Console.WriteLine($"ответ: {status.State} - {status.Message}");

    if (status.Release is { } release)
    {
        Console.WriteLine($"выпуск: {release.Tag}, файлов приложено: {release.Assets.Count}");
        Console.WriteLine($"архив: {release.Package?.Name ?? "(нет)"}");
        Console.WriteLine($"суммы: {release.Checksums?.Name ?? "(нет)"}");
    }

    if (full && status.Release is { } toDownload)
    {
        var progress = new Progress<double>(v =>
        {
            if (Math.Abs(v * 100 % 25) < 0.5) Console.WriteLine($"   скачано {v * 100:0}%");
        });

        var result = await updates.DownloadAsync(toDownload, progress);
        Console.WriteLine($"скачивание: успех = {result.Success}, {result.Message}");

        if (result.Folder is not null)
        {
            var files = Directory.GetFiles(result.Folder).Select(Path.GetFileName).ToList();
            Console.WriteLine($"распаковано файлов: {files.Count} - {string.Join(", ", files)}");
            Console.WriteLine($"сумма архива: {result.Sha256}");
        }
    }

    Console.WriteLine("--- журнал ---");
    foreach (var entry in log.Entries.Reverse()) Console.WriteLine($"   {entry.Line}");
}

static async Task Bench(string savePath)
{
    var sw = new System.Diagnostics.Stopwatch();
    double Ms() => sw.Elapsed.TotalMilliseconds;

    Console.WriteLine($"Файл: {new FileInfo(savePath).Length / 1024 / 1024} МБ");

    // Чтение: первый раз - холодный кэш ОС, дальше тёплый.
    for (var i = 0; i < 3; i++)
    {
        sw.Restart();
        var bytes = await Sl2File.ReadAllBytesSharedAsync(savePath);
        Console.WriteLine($"чтение файла #{i + 1}: {Ms():0} мс ({bytes.Length} байт)");
    }

    var data = await Sl2File.ReadAllBytesSharedAsync(savePath);

    for (var i = 0; i < 3; i++)
    {
        sw.Restart();
        var ctx = SaveContextReader.Read(data);
        Console.WriteLine($"разбор сейва #{i + 1}: {Ms():0} мс -> {ctx?.Location?.Display}");
    }

    for (var i = 0; i < 3; i++)
    {
        sw.Restart();
        var report = Sl2File.CheckIntegrity(data);
        Console.WriteLine($"целостность (11 x MD5) #{i + 1}: {Ms():0} мс, всё ок = {report.AllOk}");
    }

    sw.Restart();
    _ = System.Security.Cryptography.SHA256.HashData(data);
    Console.WriteLine($"SHA-256 одного файла: {Ms():0} мс");

    var temp = Path.Combine(Path.GetTempPath(), "erdtree-keeper-bench", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var service = new SnapshotService(new ActivityLog());
    try
    {
        for (var i = 0; i < 3; i++)
        {
            sw.Restart();
            var result = await service.CreateAsync(savePath, temp, $"bench-{i}.sl2", overwrite: true);
            Console.WriteLine($"создание снимка #{i + 1}: {Ms():0} мс, успех = {result.Success}");
        }

        var game = Path.Combine(temp, "game");
        Directory.CreateDirectory(game);
        var gameSave = Path.Combine(game, "ER0000.sl2");
        File.Copy(savePath, gameSave);
        sw.Restart();
        var restore = await service.RestoreAsync(Path.Combine(temp, "bench-0.sl2"), gameSave);
        Console.WriteLine($"восстановление в игру (с резервной копией): {Ms():0} мс, успех = {restore.Success}");

        // Большая папка: 500 снимков. Список и естественная сортировка.
        var many = Path.Combine(temp, "many");
        Directory.CreateDirectory(many);
        for (var i = 0; i < 500; i++)
            File.WriteAllBytes(Path.Combine(many, $"DLC_Место {i % 40}_{2026:0000}-08-{i % 28 + 1:00}_{i % 24:00}-{i % 60:00}-00.sl2"), []);
        sw.Restart();
        var list = service.List(many, GameProfiles.EldenRing);
        var listMs = Ms();
        sw.Restart();
        var sorted = list.OrderBy(x => x.Name, NaturalFileNameComparer.Instance).ToList();
        Console.WriteLine($"список из {list.Count} файлов: {listMs:0} мс, естественная сортировка: {Ms():0} мс ({sorted.Count})");

        sw.Restart();
        var removed = service.Rotate(many, 10, GameProfiles.EldenRing);
        Console.WriteLine($"ротация 500 -> 10: {Ms():0} мс, удалено {removed}");
    }
    finally
    {
        try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
    }

    // Память: десять чтений подряд не должны накапливаться.
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = GC.GetTotalMemory(true) / 1024 / 1024;
    for (var i = 0; i < 10; i++)
    {
        var b = await Sl2File.ReadAllBytesSharedAsync(savePath);
        _ = SaveContextReader.Read(b);
    }
    var peak = GC.GetTotalMemory(false) / 1024 / 1024;
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var after = GC.GetTotalMemory(true) / 1024 / 1024;
    Console.WriteLine($"память: до {before} МБ, пик после 10 чтений {peak} МБ, после сборки {after} МБ");
    Console.WriteLine($"рабочий набор процесса: {Environment.WorkingSet / 1024 / 1024} МБ");
}

static void Shoot(string outputDir, int width, int height)
{
    // Снимки для руководства Steam - полный набор окон.
    if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_STEAM") is { Length: > 0 } steamFixture)
    {
        ShootSteam(steamFixture, outputDir,
            Environment.GetEnvironmentVariable("ERDTREE_KEEPER_UI_LANG") == "en");
        return;
    }

    // Снимки для карточки на сайте - отдельный набор со своей фикстурой.
    if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_SHOWCASE") is { Length: > 0 } showcase)
    {
        ShootShowcase(showcase, outputDir,
            Environment.GetEnvironmentVariable("ERDTREE_KEEPER_UI_LANG") == "en");
        return;
    }

    // Проверка перехвата: исключение в UI-потоке не должно ронять процесс,
    // а должно оставить запись в журнале аварий.
    if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_CRASHTEST") == "1")
    {
        CrashGuard.SuppressDialogs = true;
        CrashGuard.InstallDispatcherHandler();
        var logPath = Path.Combine(PortableSettings.AppFolder, CrashGuard.LogFileName);
        File.Delete(logPath);

        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("нарочно: проверка перехвата"));
        for (var i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }

        var logged = File.Exists(logPath) && File.ReadAllText(logPath).Contains("проверка перехвата");
        Console.WriteLine($"   перехвачено: {CrashGuard.Caught}, журнал аварий записан: {logged}");
        Console.WriteLine(CrashGuard.Caught == 1 && logged
            ? "   ПРОВЕРКА ПЕРЕХВАТА ПРОЙДЕНА: процесс жив, след оставлен"
            : "   ПРОВЕРКА ПЕРЕХВАТА НЕ ПРОЙДЕНА");
        File.Delete(logPath);
        return;
    }

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
        Dialogs.CreateTransparencyWindow(model.SettingsPath, model.SnapshotFolder, model.SavesRoot),
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

    // Переключение языка в уже открытом окне. Снимки выше делаются на модели,
    // созданной сразу на нужном языке, и не проверяют главного: что окно
    // переключается целиком, а не наполовину.
    var switched = NewModel();
    switched.DismissOnboardingCommand.Execute(null);
    UseFakeAccount(switched);
    switched.AnalyzeCommand.Execute(null);
    for (var i = 0; i < 30 && switched.SaveContext is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }
    var switchedWindow = new MainWindow { DataContext = switched, Width = width, Height = height };
    Capture(switchedWindow, "11-язык-переключён.png", outputDir, _ =>
    {
        // Половины переключателя ведут себя как радиокнопки: язык включают,
        // а не выключают - поэтому щёлкаем именно по нужной.
        if (switched.IsEnglish) switched.IsRussian = true;
        else switched.IsEnglish = true;
    });
    Console.WriteLine($"   после переключения: язык {(switched.IsEnglish ? "En" : "Ru")}, "
                      + $"колонка \"{switched.NameSortLabel}\", кнопка \"{switched.DeleteLabel}\", "
                      + $"источники [{string.Join(", ", switched.SnapshotSources)}]");

    // Выбор должен пережить перезапуск: следующая модель читает его из файла
    // настроек, а не из того, что осталось в памяти.
    var chosen = switched.IsEnglish;

    // Как при настоящем запуске: до чтения настроек язык берётся у системы и
    // может отличаться от выбранного. Строки, созданные до чтения настроек,
    // обязаны перейти на выбранный язык - иначе список источников оставался
    // на языке системы.
    Loc.Current.Language = chosen ? Lang.Ru : Lang.En;
    var reopened = new MainViewModel();
    var expectedSource = Loc.Get(chosen ? Lang.En : Lang.Ru, "list.manual");
    Console.WriteLine($"   после перезапуска: {(reopened.IsEnglish ? "En" : "Ru")}"
                      + $" (выбирали {(chosen ? "En" : "Ru")}), список: {reopened.SnapshotSources[0]}");
    Console.WriteLine(reopened.SnapshotSources[0] == expectedSource && reopened.IsEnglish == chosen
        ? "   ПРОВЕРКА ЯЗЫКА ПРОЙДЕНА: список источников на выбранном языке"
        : "   ПРОВЕРКА ЯЗЫКА НЕ ПРОЙДЕНА: список на языке системы");

    // Разведка: что за место в каждом сейве. Нужна, чтобы для проверки ниже
    // выбрать два файла с РАЗНОЙ локацией - иначе проверка ничего не докажет.
    var probeDir = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_PROBE_DIR");
    if (!string.IsNullOrWhiteSpace(probeDir) && Directory.Exists(probeDir))
    {
        foreach (var file in Directory.GetFiles(probeDir, "*.sl2").Take(8))
        {
            try
            {
                var context = SaveContextReader.Read(File.ReadAllBytes(file));
                Console.WriteLine($"   ПРОБА {Path.GetFileName(file)}: {context?.Location?.Display ?? "(не прочиталось)"}");
            }
            catch (Exception error)
            {
                Console.WriteLine($"   ПРОБА {Path.GetFileName(file)}: ошибка {error.GetType().Name}");
            }
        }
    }

    // Проверка поведения, а не вида: игра переписала сейв - "+ локация"
    // обязана подставить новое место, не требуя нажимать "Прочитать сейв".
    var second = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_SECOND_SAVE");
    var fake = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_FAKE_SAVES");
    if (!string.IsNullOrWhiteSpace(second) && File.Exists(second) && !string.IsNullOrWhiteSpace(fake))
    {
        var live = NewModel();
        live.DismissOnboardingCommand.Execute(null);
        UseFakeAccount(live);
        live.AnalyzeCommand.Execute(null);
        for (var i = 0; i < 40 && live.SaveContext is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }

        var before = live.SaveContext?.Location?.Display ?? "(не прочитано)";

        // Подменяем файл - это ровно то, что делает игра, записывая сейв.
        var target = Directory.GetFiles(Path.Combine(fake, "76561190000000001"), "*.sl2")[0];
        File.Copy(second, target, overwrite: true);

        live.SnapshotName = "";
        live.AddLocationCommand.Execute(null);
        for (var i = 0; i < 60 && live.SnapshotName.Length == 0; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }

        var after = live.SaveContext?.Location?.Display ?? "(не прочитано)";
        Console.WriteLine($"   было: {before}");
        Console.WriteLine($"   стало после подмены сейва: {after}");
        Console.WriteLine($"   имя снимка от кнопки: {live.SnapshotName}");
        Console.WriteLine(before != after && live.SnapshotName.Contains(after)
            ? "   ПРОВЕРКА ПРОЙДЕНА: кнопка подставила свежее место без Прочитать сейв"
            : "   ПРОВЕРКА НЕ ПРОЙДЕНА");
    }

    // Второй путь: без единого нажатия. Игра дописала сейв - через несколько
    // секунд карточка и кнопки должны знать новое место сами.
    var firstSave = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_FIRST_SAVE");
    if (!string.IsNullOrWhiteSpace(second) && File.Exists(second)
        && !string.IsNullOrWhiteSpace(firstSave) && File.Exists(firstSave)
        && !string.IsNullOrWhiteSpace(fake))
    {
        // Первая проверка уже подменила файл - возвращаем исходный, иначе
        // "подменили на то же самое" выглядит как "ничего не обновилось".
        File.Copy(firstSave, Directory.GetFiles(Path.Combine(fake, "76561190000000001"), "*.sl2")[0], overwrite: true);

        var watcher = NewModel();
        watcher.DismissOnboardingCommand.Execute(null);
        UseFakeAccount(watcher);
        watcher.AnalyzeCommand.Execute(null);
        for (var i = 0; i < 40 && watcher.SaveContext is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }

        var startingPlace = watcher.SaveContext?.Location?.Display ?? "";
        var target = Directory.GetFiles(Path.Combine(fake, "76561190000000001"), "*.sl2")[0];
        File.Copy(second, target, overwrite: true);

        // Тик раз в 2 с, запись считается законченной через 6 с покоя.
        var waited = 0;
        while (waited < 20000 && (watcher.SaveContext?.Location?.Display ?? "") == startingPlace)
        {
            watcher.PollSaveFile();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(250);
            waited += 250;
        }

        var nowPlace = watcher.SaveContext?.Location?.Display ?? "(нет)";
        Console.WriteLine($"   без нажатий: было \"{startingPlace}\", стало \"{nowPlace}\" за {waited / 1000.0:0.0} с");
        Console.WriteLine(nowPlace != startingPlace
            ? "   ПРОВЕРКА 2 ПРОЙДЕНА: место обновилось само, по факту записи"
            : "   ПРОВЕРКА 2 НЕ ПРОЙДЕНА: за 20 с место не обновилось");
    }

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

    // ─── Игры ───────────────────────────────────────────────────────

    // Окно добавления игры. Список игр Steam читается с диска, поэтому на
    // машине без Steam он честно окажется пустым - окно от этого не ломается.
    Capture(
        AddGameDialog.Create((_, _) => Task.FromResult<string?>(null), _ => { }),
        "12-добавить-игру.png", outputDir);

    // Переключение на другую игру: папка снимков и список обязаны смениться,
    // иначе копии разных игр смешаются в одном списке.
    var games = NewModel();
    games.DismissOnboardingCommand.Execute(null);
    UseFakeAccount(games);

    var eldenRingFolder = games.SnapshotFolder;
    var eldenRingCount = games.Snapshots.Count;

    var darkSouls = games.Games.First(g => g.Id == "dark-souls-3");
    games.SelectedGame = darkSouls;
    for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }

    Console.WriteLine($"   игра: {games.Game.Name}");
    Console.WriteLine($"   папка снимков: {eldenRingFolder}");
    Console.WriteLine($"                  -> {games.SnapshotFolder}");
    Console.WriteLine($"   снимков в списке: {eldenRingCount} -> {games.Snapshots.Count}");
    Console.WriteLine($"   кнопка \"+ локация\" доступна: {games.AddLocationCommand.CanExecute(null)}");
    Console.WriteLine($"   что проверяет кнопка целостности: {games.IntegrityNote}");

    var foldersDiffer = !string.Equals(eldenRingFolder, games.SnapshotFolder, StringComparison.OrdinalIgnoreCase);
    var mapNamesOff = !games.AddLocationCommand.CanExecute(null);
    Console.WriteLine(foldersDiffer && mapNamesOff
        ? "   ПРОВЕРКА ИГР ПРОЙДЕНА: своя папка, кнопки места выключены"
        : "   ПРОВЕРКА ИГР НЕ ПРОЙДЕНА");

    Capture(
        new MainWindow { DataContext = games, Width = width, Height = height },
        "13-другая-игра.png", outputDir);

    // Возвращаем Elden Ring: настройки общие, и следующий запуск стенда
    // должен начинаться с того же, с чего начинался этот.
    games.SelectedGame = games.Games.First(g => g.Id == GameProfiles.EldenRingId);

    // ─── Обновления ─────────────────────────────────────────────────

    // Окно обновлений в исходном состоянии: разрешения нет, и оно спрашивает.
    // Сеть при этом не трогается - снимок можно делать без интернета.
    var updates = NewModel();
    Capture(
        UpdateDialog.Create(updates, () => { }),
        "14-обновления.png", outputDir);

    // Главное обещание про сеть: без разрешения запуск программы не порождает
    // ни одного обращения наружу. Проверяется по журналу, где сетевые записи
    // идут отдельной меткой.
    var quiet = NewModel();
    quiet.DismissOnboardingCommand.Execute(null);
    UseFakeAccount(quiet);

    var load = quiet.LoadAsync();
    for (var i = 0; i < 40 && !load.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(100); }

    var networkEntries = quiet.Log.Entries.Count(e => e.Kind == ActivityKind.Network);
    Console.WriteLine($"   разрешение на сеть: {(quiet.UpdatesAllowed is null ? "не спрашивали" : quiet.UpdatesAllowed.ToString())}");
    Console.WriteLine($"   сетевых записей в журнале после запуска: {networkEntries}");
    Console.WriteLine(networkEntries == 0
        ? "   ПРОВЕРКА СЕТИ ПРОЙДЕНА: без разрешения ни одного запроса"
        : "   ПРОВЕРКА СЕТИ НЕ ПРОЙДЕНА");

    InstallTest();

    Console.WriteLine($"Готово: {Path.GetFullPath(outputDir)}");
}


// Настоящий SteamID64 резолвится в публичный профиль, поэтому на снимках
// экрана стоит вымышленный: он ниже диапазона существующих аккаунтов.
/// <summary>Модель окна на запрошенном языке, а не на том, что в настройках.</summary>
static MainViewModel NewModel()
{
    var model = new MainViewModel();

    // Язык переключается так же, как кнопкой в окне: модель держит снимок
    // таблицы строк, и подмены Loc.Current.Language мало - разметка осталась
    // бы на прежнем языке, а сообщения перешли на новый.
    if (Environment.GetEnvironmentVariable("ERDTREE_KEEPER_UI_LANG") == "en") model.IsEnglish = true;
    else model.IsRussian = true;

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
