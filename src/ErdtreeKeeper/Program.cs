using System.Runtime.InteropServices;
using Avalonia;

namespace ErdtreeKeeper;

internal static class Program
{
    /// <summary>
    /// Нативные библиотеки отрисовки. Без них Avalonia падает при инициализации,
    /// ещё до того как появится хоть какое-то окно.
    /// </summary>
    private static readonly string[] RequiredLibraries =
    [
        "libSkiaSharp.dll",
        "libHarfBuzzSharp.dll",
        "av_libglesv2.dll",
    ];

    /// <summary>
    /// Точка входа.
    ///
    /// Проект собирается как WinExe, поэтому у программы нет консоли: чёрное
    /// окно позади интерфейса, за которое ругали прежнюю версию на .bat, здесь
    /// невозможно в принципе.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        if (!CheckLibraries()) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Проверяет, что рядом лежат библиотеки отрисовки.
    ///
    /// Если распаковать из архива один exe - а так делают - программа молча
    /// завершалась с ошибкой инициализации: консоли нет, окно ещё не создано,
    /// и пользователь видел ровно ничего. Для программы, которую и без того
    /// подозревают в вирусах, "запустил, и не открылось" - худший исход.
    ///
    /// Сообщение показываем средствами системы: Avalonia на этом этапе
    /// заведомо не поднимется.
    /// </summary>
    private static bool CheckLibraries()
    {
        var folder = AppContext.BaseDirectory;
        var missing = RequiredLibraries
            .Where(name => !File.Exists(Path.Combine(folder, name)))
            .ToArray();

        if (missing.Length == 0) return true;

        MessageBox(
            IntPtr.Zero,
            $"""
             Рядом с программой не хватает файлов:

             {string.Join(Environment.NewLine, missing)}

             Скорее всего, из архива распакован только ErdtreeKeeper.exe.
             Распакуйте архив целиком - библиотеки должны лежать в одной папке
             с программой.

             Папка: {folder}
             """,
            $"{AppInfo.Name}: не хватает файлов",
            MB_ICONERROR | MB_OK);

        return false;
    }

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
