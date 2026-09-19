using ErdtreeKeeper.Core;
using ErdtreeKeeper.Updates;
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
        // Настройки читаются позже, уже в модели окна, а сообщение о нехватке
        // библиотек может понадобиться прямо сейчас - поэтому язык сначала
        // берётся из системы, а выбор пользователя применяется поверх.
        Loc.Current.Language = Loc.DetectFromSystem();

        // Установщик обновления - это та же программа, запущенная с ключом из
        // временной папки. Окна у неё нет: она ждёт закрытия прежней копии,
        // переносит файлы и запускает обновлённую.
        if (UpdateInstaller.TryRunAsInstaller(args)) return;

        if (!CheckLibraries()) return;

        // После удачного обновления временные папки остаются - убрать их может
        // только уже запущенная новая копия, изнутри установщика это невозможно.
        UpdateInstaller.CleanupLeftovers();

        // Раньше любого кода, который может упасть: сбой должен оставить след
        // и окно, а не закрыть программу молча.
        CrashGuard.Install();

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

        Native.MessageBox(
            IntPtr.Zero,
            Loc.Get("startup.missingBody", string.Join(Environment.NewLine, missing), folder),
            Loc.Get("startup.missingTitle", AppInfo.Name),
            Native.IconError | Native.Ok);

        return false;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
