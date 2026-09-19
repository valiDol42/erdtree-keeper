namespace ErdtreeKeeper.Core;

/// <summary>
/// Что программа может сказать о файле сохранения - ровно столько, сколько
/// действительно проверила.
///
/// Глубина проверки зависит от игры, и это не мелочь, а обещание. Для Elden
/// Ring пересчитываются все одиннадцать контрольных сумм, и "цел" здесь
/// означает "игра его загрузит". Для остальных игр FromSoftware проверяется
/// структура контейнера: обрезанный или чужой файл виден, а вот испорченные
/// данные внутри - нет. Для игры, добавленной вручную, известно лишь то, что
/// копия совпала с источником побайтово.
///
/// Текст отчёта составляется здесь же, а не в окне: тогда его можно проверить
/// тестом, а не глазами.
/// </summary>
public sealed record SaveCheck(
    SaveLayout Layout,
    string? Problem,
    long Size,
    Sl2File.IntegrityReport? EldenRing = null,
    Bnd4File.Structure? Container = null)
{
    /// <summary>Файл годен к восстановлению в игру.</summary>
    public bool Ok => Problem is null;
}

public static class SaveIntegrity
{
    /// <summary>
    /// Проверяет файл настолько, насколько позволяет формат игры.
    ///
    /// Отказ здесь означает "в игру это писать нельзя". Всё остальное -
    /// предупреждения в отчёте: недосказанность лучше, чем запрет на
    /// восстановление собственного файла из-за того, что программа его формат
    /// не понимает.
    /// </summary>
    public static SaveCheck Inspect(GameProfile game, byte[] data)
    {
        if (data.LongLength == 0)
        {
            return new SaveCheck(game.Layout, Loc.Get("problem.empty"), 0);
        }

        switch (game.Layout)
        {
            case SaveLayout.EldenRing:
            {
                var report = Sl2File.CheckIntegrity(data);
                return new SaveCheck(game.Layout, report.AllOk ? null : report.Problem,
                    data.LongLength, EldenRing: report);
            }

            case SaveLayout.Bnd4:
            {
                var structure = Bnd4File.Inspect(data);
                string? problem = null;
                if (!structure.Recognised) problem = Loc.Get("problem.notBnd4");
                else if (!structure.HeadersFit) problem = Loc.Get("problem.bnd4Truncated");

                return new SaveCheck(game.Layout, problem, data.LongLength, Container: structure);
            }

            default:
                return new SaveCheck(game.Layout, null, data.LongLength);
        }
    }

    /// <summary>
    /// Отчёт для окна: что за файл, что именно проверено и чего проверка не
    /// покрывает.
    /// </summary>
    public static string BuildReport(GameProfile game, SaveCheck check, string fileName)
    {
        var lines = new List<string> { Loc.Get("report.file", fileName), Loc.Get("report.game", game.Name), "" };

        if (check.Layout == SaveLayout.EldenRing && check.EldenRing is { } report)
        {
            AppendEldenRing(lines, report);
            return string.Join(Environment.NewLine, lines);
        }

        if (check.Layout == SaveLayout.Bnd4)
        {
            AppendContainer(lines, check);
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(Loc.Get("report.size", check.Size.ToString("N0")));
        lines.Add("");
        lines.Add(Loc.Get("report.opaque"));
        lines.Add("");
        lines.Add(check.Ok ? Loc.Get("report.opaqueVerdict") : Loc.Get("report.emptyVerdict"));
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendEldenRing(List<string> lines, Sl2File.IntegrityReport report)
    {
        if (!report.FileRecognised)
        {
            lines.Add(Loc.Get("report.notEldenRing"));
            return;
        }

        lines.Add(report.SizeAsExpected
            ? Loc.Get("report.sizeNormal", report.ActualSize.ToString("N0"))
            : Loc.Get("report.sizeOdd", report.ActualSize.ToString("N0"), Sl2File.VanillaSize.ToString("N0")));
        lines.Add("");
        lines.Add(Loc.Get("report.howItWorks1"));
        lines.Add(Loc.Get("report.howItWorks2"));
        lines.Add("");

        foreach (var block in report.Blocks)
        {
            lines.Add(block.Ok
                ? $"  {block.Title,-10}  " + Loc.Get("report.blockOk")
                : $"  {block.Title,-10}  " + Loc.Get("report.blockBad", block.Stored[..8], block.Actual[..8]));
        }

        lines.Add("");
        lines.Add(report.AllOk
            ? Loc.Get("report.verdictOk")
            : Loc.Get("report.verdictBad", report.BadCount));

        if (!report.AllOk) lines.Add(Loc.Get("report.verdictBad2"));
    }

    private static void AppendContainer(List<string> lines, SaveCheck check)
    {
        var structure = check.Container;
        lines.Add(Loc.Get("report.size", check.Size.ToString("N0")));

        if (structure is null || !structure.Recognised)
        {
            lines.Add("");
            lines.Add(Loc.Get("report.notBnd4"));
            return;
        }

        lines.Add(Loc.Get("report.bnd4Version", structure.Version.Length > 0 ? structure.Version : "-"));
        lines.Add(Loc.Get("report.bnd4Entries", structure.FileCount));
        lines.Add("");
        lines.Add(Loc.Get("report.bnd4Explain"));
        lines.Add("");
        lines.Add(structure.HeadersFit
            ? Loc.Get("report.bnd4Ok")
            : Loc.Get("report.bnd4Truncated"));
    }
}
