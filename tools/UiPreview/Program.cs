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
    var reopened = new MainViewModel();
    Console.WriteLine($"   после перезапуска: {(reopened.IsEnglish ? "En" : "Ru")}"
                      + $" (выбирали {(chosen ? "En" : "Ru")})");

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
