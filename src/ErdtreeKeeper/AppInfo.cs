using System.Reflection;

namespace ErdtreeKeeper;

/// <summary>Кто это написал и какая это версия - для подписи внизу окна и окна "О программе".</summary>
public static class AppInfo
{
    public const string Name = "Erdtree Keeper";
    public const string Author = "Krut42";

    /// <summary>Версия без хвоста: "1.0.0".</summary>
    public static string Version { get; } = Split().Version;

    /// <summary>Короткий хеш коммита, из которого собран файл. Пусто, если неизвестен.</summary>
    public static string Commit { get; } = Split().Commit;

    /// <summary>
    /// Версия так, как её стоит показывать: "1.0.0 (сборка a1b2c3d)".
    ///
    /// Хеш нужен, чтобы две сборки одной версии можно было различить - иначе
    /// непонятно, обновился ты или запускаешь вчерашний файл.
    /// </summary>
    public static string FullVersion =>
        Commit.Length > 0 ? $"{Version} (сборка {Commit})" : Version;

    private static (string Version, string Commit) Split()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0.0";

        // Компилятор дописывает хеш коммита после "+".
        var plus = raw.IndexOf('+');
        return plus > 0 ? (raw[..plus], raw[(plus + 1)..]) : (raw, "");
    }
}
