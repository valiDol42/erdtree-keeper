using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ErdtreeKeeper;
using ErdtreeKeeper.ViewModels;
using ErdtreeKeeper.Views;

// Рисует окна приложения в файлы, не открывая их на рабочем столе.
// Запуск: dotnet run --project tools/UiPreview -- <папка> [ширина] [высота]

var outputDir = args.Length > 0 ? args[0] : "ui-preview";
var width = args.Length > 1 && int.TryParse(args[1], out var w) ? w : 1180;
var height = args.Length > 2 && int.TryParse(args[2], out var h) ? h : 840;

Directory.CreateDirectory(outputDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .Start((_, _) => Shoot(outputDir, width, height), args);

static void Shoot(string outputDir, int width, int height)
{
    // Первый запуск: с приветственным экраном.
    var first = new MainWindow { DataContext = new MainViewModel(), Width = width, Height = height };
    Capture(first, "01-первый-запуск.png", outputDir);

    // Обычный вид.
    var model = new MainViewModel();
    model.DismissOnboardingCommand.Execute(null);
    model.RefreshAccounts();
    var main = new MainWindow { DataContext = model, Width = width, Height = height };
    Capture(main, "02-главный-экран.png", outputDir);

    // Диалоги. Пути настоящие - в них видно, что ничего не зашито жёстко.
    Capture(
        Dialogs.CreateTransparencyWindow(model.SettingsPath, model.SnapshotFolder),
        "04-что-программа-делает.png", outputDir);

    // Список переключён на автосохранения, настройки раскрыты.
    var auto = new MainViewModel();
    auto.DismissOnboardingCommand.Execute(null);
    auto.RefreshAccounts();
    auto.AutoSnapshotEnabled = true;
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
    var withLog = new MainViewModel();
    withLog.DismissOnboardingCommand.Execute(null);
    withLog.RefreshAccounts();
    var logWindow = new MainWindow { DataContext = withLog, Width = width, Height = height };
    logWindow.Opened += (_, _) =>
    {
        var toggle = logWindow.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LogToggle");
        if (toggle is not null) toggle.IsChecked = true;
    };
    Capture(logWindow, "03-журнал-открыт.png", outputDir);

    Capture(
        Dialogs.CreateAboutWindow(model.SettingsPath, model.IsPortable, model.SettingsFileState),
        "05-о-программе.png", outputDir);

    Capture(
        Dialogs.CreateReportWindow("Проверка целостности", SampleReport()),
        "05-проверка-целостности.png", outputDir);

    Capture(
        Dialogs.CreateReportWindow("Пример длинной строки", string.Join("\n", Enumerable.Repeat(
            "Очень длинная строка, которая заведомо шире окна и должна прокручиваться, а не обрезаться молча.", 3))),
        "06-длинный-текст.png", outputDir);

    Console.WriteLine($"Готово: {Path.GetFullPath(outputDir)}");
}

static string SampleReport() => string.Join(Environment.NewLine,
[
    "Файл: ER0000.sl2",
    "",
    "Размер: 28 967 888 байт - как у обычного сейва.",
    "",
    "Игра проверяет каждый блок по контрольной сумме MD5 и отказывается",
    "загружать блок, если сумма не сошлась. Ниже - результат по каждому.",
    "",
    "  Слот 1      в порядке",
    "  Слот 2      в порядке",
    "  Слот 3      ПОВРЕЖДЁН  (записано 5b8259aa, посчитано 8354dcaa)",
    "  Профиль     в порядке",
    "",
    "Итог: повреждённых блоков 1.",
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
