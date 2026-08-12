using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ErdtreeKeeper;
using ErdtreeKeeper.ViewModels;
using ErdtreeKeeper.Views;

// Рисует окно приложения в файл. Запуск:
//   dotnet run --project tools/UiPreview -- <папка> [ширина] [высота]

var outputDir = args.Length > 0 ? args[0] : "ui-preview";
var width = args.Length > 1 && int.TryParse(args[1], out var w) ? w : 1180;
var height = args.Length > 2 && int.TryParse(args[2], out var h) ? h : 800;

Directory.CreateDirectory(outputDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .AfterSetup(_ => { })
    .Start((application, _) =>
    {
        Shoot(application, outputDir, width, height);
    }, args);

static void Shoot(Application application, string outputDir, int width, int height)
{
    // Первый запуск: с приветственным экраном.
    Capture(new MainViewModel(), "01-первый-запуск.png", outputDir, width, height);

    // Обычный вид: приветствие уже закрыто.
    var main = new MainViewModel();
    main.DismissOnboardingCommand.Execute(null);
    main.RefreshAccounts();
    Capture(main, "02-главный-экран.png", outputDir, width, height);

    Console.WriteLine($"Готово: {Path.GetFullPath(outputDir)}");
}

static void Capture(MainViewModel viewModel, string name, string outputDir, int width, int height)
{
    var window = new MainWindow
    {
        DataContext = viewModel,
        Width = width,
        Height = height,
    };

    window.Show();

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
    Console.WriteLine($"{name}: {new FileInfo(path).Length / 1024} КБ");

    window.Close();
}
