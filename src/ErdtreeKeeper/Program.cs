using Avalonia;

namespace ErdtreeKeeper;

internal static class Program
{
    /// <summary>
    /// Точка входа.
    ///
    /// Проект собирается как WinExe, поэтому у программы нет консоли: чёрное
    /// окно позади интерфейса, за которое ругали прежнюю версию на .bat, здесь
    /// невозможно в принципе.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
