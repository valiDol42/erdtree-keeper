using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using ErdtreeKeeper.Core;

namespace ErdtreeKeeper;

/// <summary>
/// Последний рубеж: что делать с исключением, которое никто не поймал.
///
/// Без этого класса непредвиденная ошибка в обработчике клика или в таймере
/// закрывала окно молча. Для программы, которую и так подозревают в вирусах,
/// "нажал - и всё пропало" - худшее, что может случиться: игрок не узнает ни
/// что произошло, ни что его файлы целы.
///
/// Поэтому три вещи, в этом порядке: записать подробности в файл рядом с
/// программой (его можно приложить к сообщению об ошибке), показать окно
/// системными средствами (они работают, даже когда Avalonia уже не в
/// состоянии), и, где это возможно, не закрывать программу.
/// </summary>
public static class CrashGuard
{
    public const string LogFileName = "erdtree-keeper-crash.log";

    private static int _installed;
    private static int _dispatcherInstalled;

    /// <summary>
    /// Не показывать системное окно. Нужно стенду и тестам: они нарочно
    /// роняют обработчик и не могут нажать "ОК" в модальном окне.
    /// </summary>
    public static bool SuppressDialogs { get; set; }

    /// <summary>Сколько сбоев перехвачено с запуска - стенд проверяет по нему, что перехват сработал.</summary>
    public static int Caught { get; private set; }

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        // Исключение вне UI-потока. Здесь процесс всё равно умрёт - .NET не
        // даёт этого отменить, - но след останется, и окно человек увидит.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()),
                "AppDomain", fatal: true);

        // Задача, результат которой никто не дождался. Процесс не закрывается,
        // но ошибка иначе исчезла бы без следа.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception.GetBaseException(), "Task", fatal: false);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Ошибки UI-потока: клики, таймер, привязки. Подключается отдельно,
    /// потому что диспетчер существует только после старта Avalonia.
    /// </summary>
    public static void InstallDispatcherHandler()
    {
        // Подписка ровно одна: App подписывается при старте, стенд - ещё раз
        // сам, и без этой защиты одно исключение отчитывалось бы дважды.
        if (Interlocked.Exchange(ref _dispatcherInstalled, 1) == 1) return;

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Report(e.Exception, "UI", fatal: false);
            // Окно остаётся открытым: одна неудачная кнопка не повод терять
            // всё, что человек делал.
            e.Handled = true;
        };
    }

    private static void Report(Exception exception, string origin, bool fatal)
    {
        Caught++;
        var logPath = WriteLog(exception, origin);
        if (SuppressDialogs) return;

        var text = new StringBuilder()
            .AppendLine(Loc.Get("crash.body"))
            .AppendLine()
            .AppendLine(exception.GetType().Name + ": " + exception.Message)
            .AppendLine()
            .AppendLine(Loc.Get("crash.logAt", logPath ?? Loc.Get("crash.logFailed")))
            .ToString();

        if (fatal) text += Environment.NewLine + Loc.Get("crash.willClose");

        try
        {
            MessageBox(IntPtr.Zero, text, Loc.Get("crash.title"), MB_ICONERROR | MB_OK);
        }
        catch
        {
            // Даже окно не показалось - хотя бы файл написан.
        }
    }

    /// <summary>
    /// Пишет подробности рядом с программой. Если рядом нельзя - во временную
    /// папку: сообщение об ошибке важнее того, где именно оно лежит.
    /// </summary>
    private static string? WriteLog(Exception exception, string origin)
    {
        var entry = new StringBuilder()
            .AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {AppInfo.Name} {AppInfo.FullVersion} ===")
            .AppendLine($"origin: {origin}")
            .AppendLine($"os: {Environment.OSVersion}, .NET {Environment.Version}")
            .AppendLine(exception.ToString())
            .AppendLine()
            .ToString();

        foreach (var folder in new[] { PortableSettings.AppFolder, Path.GetTempPath() })
        {
            try
            {
                var path = Path.Combine(folder, LogFileName);
                File.AppendAllText(path, entry, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Следующая папка.
            }
        }

        return null;
    }

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
