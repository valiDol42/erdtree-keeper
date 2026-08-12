using System.Diagnostics;

namespace ErdtreeKeeper.Views;

/// <summary>
/// Ссылки наружу.
///
/// Программа не открывает сетевых соединений и не умеет этого делать - в ней
/// нет сетевых библиотек. Ссылка передаётся системе, и страницу открывает
/// браузер пользователя, уже после явного щелчка. Обещание "не выходит в
/// интернет" от этого в силе, но формулировать его надо именно так.
/// </summary>
public static class ExternalLinks
{
    /// <summary>Карта и трекер прогресса, откуда взят справочник локаций.</summary>
    public const string Site = "https://eldenring.krut.top";

    public const string SiteLabel = "eldenring.krut.top";

    public static void Open(string url)
    {
        // Открываем только то, что задано в коде. Ничего, пришедшего из файла
        // настроек или из сейва, сюда попасть не может.
        if (url != Site) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // Браузер не нашёлся - не повод падать.
        }
    }
}
