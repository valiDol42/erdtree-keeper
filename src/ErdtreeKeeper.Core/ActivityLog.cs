using System.Collections.ObjectModel;

namespace ErdtreeKeeper.Core;

public enum ActivityKind
{
    Info,
    Read,
    Write,
    Delete,
    Warning,
    Error,
}

/// <summary>Одна запись журнала: что программа сделала с файлами.</summary>
public sealed record ActivityEntry(DateTime At, ActivityKind Kind, string Message, string? Path = null)
{
    public string Line => Path is null
        ? $"{At:HH:mm:ss}  {Tag}  {Message}"
        : $"{At:HH:mm:ss}  {Tag}  {Message}  ->  {Path}";

    public string Tag => Kind switch
    {
        ActivityKind.Read => Loc.Get("log.read"),
        ActivityKind.Write => Loc.Get("log.write"),
        ActivityKind.Delete => Loc.Get("log.delete"),
        ActivityKind.Warning => Loc.Get("log.warn"),
        ActivityKind.Error => Loc.Get("log.error"),
        _ => Loc.Get("log.info"),
    };
}

/// <summary>
/// Журнал файловых операций.
///
/// Существует ради доверия: игрок в любой момент видит полный список того,
/// что программа трогала на диске, и может выгрузить его в файл. Приложение,
/// которое нечего скрывать, показывает свои действия целиком.
/// </summary>
public sealed class ActivityLog
{
    private const int MaxEntries = 500;

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public event Action<ActivityEntry>? Added;

    public void Add(ActivityKind kind, string message, string? path = null)
    {
        var entry = new ActivityEntry(DateTime.Now, kind, message, path);

        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);

        Added?.Invoke(entry);
    }

    public void Info(string message, string? path = null) => Add(ActivityKind.Info, message, path);
    public void Read(string message, string? path = null) => Add(ActivityKind.Read, message, path);
    public void Write(string message, string? path = null) => Add(ActivityKind.Write, message, path);
    public void Deleted(string message, string? path = null) => Add(ActivityKind.Delete, message, path);
    public void Warn(string message, string? path = null) => Add(ActivityKind.Warning, message, path);
    public void Error(string message, string? path = null) => Add(ActivityKind.Error, message, path);

    /// <summary>Выгружает журнал в текстовый файл - его можно показать кому угодно.</summary>
    public async Task ExportAsync(string path, CancellationToken ct = default)
    {
        var lines = new List<string>
        {
            Loc.Get("log.exportTitle"),
            Loc.Get("log.exportedAt", DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")),
            "",
        };

        lines.AddRange(Entries.Reverse().Select(e => $"{e.At:dd.MM.yyyy HH:mm:ss}  {e.Tag,-9}  {e.Message}"
                                                    + (e.Path is null ? "" : $"  ->  {e.Path}")));

        await File.WriteAllLinesAsync(path, lines, ct).ConfigureAwait(false);
    }
}
