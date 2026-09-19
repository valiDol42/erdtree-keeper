using System.Diagnostics;
using ErdtreeKeeper.Core;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Ссылки наружу.
///
/// Страницу открывает браузер пользователя, а не программа: ей передаётся
/// адрес, и дальше соединение устанавливает уже он. Своё соединение программа
/// открывает ровно в одном месте - при проверке обновлений, и только с
/// разрешения.
///
/// Открываются только адреса, заданные в коде, и страницы GitHub этого
/// проекта. Ничто, пришедшее из файла настроек или из сейва, сюда попасть не
/// может, а адрес из ответа сети сначала проходит проверку по списку узлов.
/// </summary>
public static class ExternalLinks
{
    /// <summary>Карта и трекер прогресса, откуда взят справочник локаций.</summary>
    public const string Site = "https://eldenring.krut.top";

    public const string SiteLabel = "eldenring.krut.top";

    /// <summary>Главная страница проектов.</summary>
    public const string Home = "https://krut.top";

    public const string HomeLabel = "krut.top";

    /// <summary>Почта для связи.</summary>
    public const string Email = "admin@krut.top";

    public const string EmailLink = "mailto:admin@krut.top";

    /// <summary>Исходный код. На нём держится всё обещание открытости.</summary>
    public const string Repository = "https://github.com/valiDol42/erdtree-keeper";

    public const string RepositoryLabel = "github.com/valiDol42/erdtree-keeper";

    public static void Open(string url)
    {
        var known = url is Site or Repository or Home or EmailLink;
        if (!known && !AppUpdate.IsTrustedUrl(url)) return;

        Start(url);
    }

    /// <summary>Открывает папку в проводнике - например, со скачанным обновлением.</summary>
    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Start(path);
    }

    private static void Start(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // Браузер или проводник не нашлись - не повод падать.
        }
    }
}
