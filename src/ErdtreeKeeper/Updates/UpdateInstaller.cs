using System.Diagnostics;
using ErdtreeKeeper.Core;

namespace ErdtreeKeeper.Updates;

/// <summary>
/// Замена файлов программы на новые.
///
/// Работающий exe не может переписать сам себя, и обычно из этого положения
/// выходят через .bat-файл, который ждёт закрытия программы и копирует файлы.
/// Здесь сделано иначе: установку выполняет сама скачанная программа,
/// запущенная с особым ключом из временной папки.
///
/// Так надёжнее и честнее. Никаких сторонних скриптов, копированием занят тот
/// же файл, чью контрольную сумму только что сверили с опубликованной, и
/// увидеть происходящее можно в журнале - а не гадать, что делает возникший из
/// ниоткуда командный файл.
/// </summary>
public static class UpdateInstaller
{
    public const string ExecutableName = "ErdtreeKeeper.exe";

    /// <summary>Ключ командной строки, по которому программа работает установщиком.</summary>
    public const string ApplySwitch = "--apply-update";

    /// <summary>Начало имени временных папок обновления - по нему они и убираются.</summary>
    private const string WorkPrefix = "ErdtreeKeeper-update-";

    /// <summary>
    /// Скопировать файлы, но не запускать программу.
    ///
    /// Нужно стенду: замену файлов надо проверять на каждой сборке, а
    /// открывающееся посреди проверки окно этому только мешает.
    /// </summary>
    public const string NoLaunchSwitch = "--no-launch";

    /// <summary>
    /// Можно ли писать в папку программы.
    ///
    /// Портативную программу нередко кладут в Program Files, где прав на
    /// запись нет. Узнать это надо до скачивания, а не после: иначе человек
    /// ждёт загрузку ради сообщения об отказе.
    /// </summary>
    public static bool CanInstallInto(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".update-probe-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Запускает установку и сообщает, можно ли закрывать окно.
    ///
    /// Дальше работает уже новый процесс: он дожидается, пока этот закроется,
    /// и только потом трогает файлы.
    /// </summary>
    public static bool Launch(string stagingFolder, string targetFolder, ActivityLog log)
    {
        var installer = Path.Combine(stagingFolder, ExecutableName);
        if (!File.Exists(installer)) return false;

        var arguments = new[]
        {
            ApplySwitch,
            "--from", stagingFolder,
            "--to", targetFolder,
            "--wait", Environment.ProcessId.ToString(),
        };

        var start = new ProcessStartInfo(installer)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            log.Info(Loc.Get("log.updLaunch"), installer);
            return Process.Start(start) is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                       or UnauthorizedAccessException)
        {
            log.Error(Loc.Get("log.updLaunchFailed", ex.Message), installer);
            return false;
        }
    }

    /// <summary>
    /// Работа в роли установщика, если так велит командная строка.
    ///
    /// Возвращает true, когда программа была запущена ради установки: окно в
    /// этом случае не открывается вовсе.
    /// </summary>
    public static bool TryRunAsInstaller(string[] args)
    {
        if (args.Length == 0 || !args.Contains(ApplySwitch)) return false;

        var from = ValueOf(args, "--from");
        var to = ValueOf(args, "--to");
        var wait = ValueOf(args, "--wait");

        if (from is null || to is null) return true;

        if (int.TryParse(wait, out var pid)) WaitForExit(pid);

        var problem = CopyAll(from, to);

        var executable = Path.Combine(to, ExecutableName);
        if (problem is null && !args.Contains(NoLaunchSwitch) && File.Exists(executable))
        {
            try
            {
                Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    WorkingDirectory = to,
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                           or UnauthorizedAccessException)
            {
                problem = ex.Message;
            }
        }

        if (problem is not null && !args.Contains(NoLaunchSwitch))
        {
            // Окна у установщика нет, и молча уйти он не может: человек ждёт
            // обновлённую программу, а она не открылась.
            Native.MessageBox(IntPtr.Zero,
                Loc.Get("upd.installFailedBody", problem, from),
                Loc.Get("upd.installFailedTitle"),
                Native.IconError | Native.Ok);
        }

        LastProblem = problem;
        return true;
    }

    /// <summary>Что помешало установке в последний раз. Пусто - всё прошло.</summary>
    public static string? LastProblem { get; private set; }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Ждёт, пока прежняя копия программы закроется и отпустит свои файлы.</summary>
    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(milliseconds: 20000);
        }
        catch (ArgumentException)
        {
            // Уже закрылась - ровно то, чего мы ждали.
        }
        catch (InvalidOperationException) { }

        // Windows отпускает файл не в тот же миг, когда процесс исчезает.
        Thread.Sleep(400);
    }

    /// <summary>
    /// Копирует новые файлы поверх старых. Возвращает описание беды или ничего.
    ///
    /// Настройки и снимки лежат в той же папке, но под своими именами, и
    /// копирование их не трогает: переносится только то, что лежало в архиве.
    /// </summary>
    private static string? CopyAll(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(to);

            foreach (var source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(from, source);
                var destination = Path.Combine(to, relative);

                var folder = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                if (!CopyWithRetries(source, destination, out var error)) return error;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Копирование с повторами: сразу после закрытия программы файл ещё
    /// занят - его держит то антивирус, то сам проводник.
    /// </summary>
    private static bool CopyWithRetries(string source, string destination, out string? error)
    {
        error = null;

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                Thread.Sleep(400);
            }
        }

        return false;
    }

    /// <summary>
    /// Убирает временные папки обновления.
    ///
    /// Удалить их изнутри установщика нельзя - он сам оттуда запущен, - зато
    /// это легко делает уже обновлённая программа при следующем старте.
    /// </summary>
    public static void CleanupLeftovers()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), WorkPrefix + "*"))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не убралось - не повод мешать запуску.
        }
    }
}
