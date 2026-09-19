using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Обновление - единственная часть программы, которая слушает сеть, поэтому
/// её разбор проверяется особенно придирчиво. Главное здесь не "работает ли
/// на хорошем ответе", а "что будет на плохом": ответ сервера - это данные, и
/// доверять им нельзя.
/// </summary>
public class AppUpdateTests
{
    public AppUpdateTests() => Loc.Current.Language = Lang.Ru;

    [Theory]
    [InlineData("1.4.1", "1.4.0", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("v1.5.0", "1.5.0", false)]
    [InlineData("1.4.1", "1.4.1", false)]
    [InlineData("1.4.0", "1.4.1", false)]
    [InlineData("2.0.0", "1.99.99", true)]
    public void Versions_are_compared_as_numbers(string candidate, string current, bool newer) =>
        Assert.Equal(newer, AppUpdate.IsNewer(candidate, current));

    /// <summary>Сравнение по алфавиту сочло бы 1.9 новее 1.10 - самая частая ошибка в таких проверках.</summary>
    [Fact]
    public void Ten_is_newer_than_nine() => Assert.True(AppUpdate.IsNewer("1.10.0", "1.9.0"));

    [Theory]
    [InlineData("v1.4.1", "1.4.1")]
    [InlineData("1.4.1", "1.4.1")]
    [InlineData("  v2.0.0 ", "2.0.0")]
    public void Tags_and_versions_mean_the_same(string tag, string version) =>
        Assert.Equal(version, AppUpdate.NormalizeVersion(tag));

    [Theory]
    [InlineData("https://api.github.com/repos/x/y/releases/latest", true)]
    [InlineData("https://github.com/valiDol42/erdtree-keeper/releases", true)]
    [InlineData("https://objects.githubusercontent.com/whatever", true)]
    [InlineData("http://github.com/x", false)]
    [InlineData("https://github.com.evil.example/x", false)]
    [InlineData("https://evil.example/github.com", false)]
    [InlineData("file:///C:/windows/system32", false)]
    [InlineData("", false)]
    public void Only_github_addresses_are_trusted(string url, bool trusted) =>
        Assert.Equal(trusted, AppUpdate.IsTrustedUrl(url));

    [Fact]
    public void A_release_is_read_from_the_answer()
    {
        var release = AppUpdate.ParseRelease(Json());

        Assert.NotNull(release);
        Assert.Equal("v1.5.0", release.Tag);
        Assert.Equal("1.5.0", release.Version);
        Assert.Contains("Что нового", release.Notes);
        Assert.NotNull(release.Package);
        Assert.Equal("ErdtreeKeeper-v1.5.0-win-x64.zip", release.Package.Name);
        Assert.NotNull(release.Checksums);
        Assert.Equal("SHA256SUMS.txt", release.Checksums.Name);
    }

    /// <summary>
    /// Ссылка на посторонний узел отбрасывается вместе с файлом. Так выглядел
    /// бы подменённый ответ: всё остальное на месте, а качать предлагают
    /// откуда-то ещё.
    /// </summary>
    [Fact]
    public void An_asset_from_a_foreign_host_is_dropped()
    {
        var json = Json().Replace(
            "https://github.com/valiDol42/erdtree-keeper/releases/download/v1.5.0/ErdtreeKeeper-v1.5.0-win-x64.zip",
            "https://evil.example/ErdtreeKeeper-v1.5.0-win-x64.zip");

        var release = AppUpdate.ParseRelease(json);

        Assert.NotNull(release);
        Assert.Null(release.Package);
    }

    /// <summary>Черновики и предварительные выпуски игрокам не предлагаются.</summary>
    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    public void Drafts_and_prereleases_are_ignored(string from, string to) =>
        Assert.Null(AppUpdate.ParseRelease(Json().Replace(from, to)));

    [Theory]
    [InlineData("не json вовсе")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\": \"\"}")]
    public void A_broken_answer_yields_nothing(string json) => Assert.Null(AppUpdate.ParseRelease(json));

    [Fact]
    public void Checksums_are_read_line_by_line()
    {
        var text = string.Join('\n',
            "# комментарий",
            "4b795f8c6b6aa060ba951edb8aa234ae467e66b6912b82d727596fc12e5ac562  ErdtreeKeeper-v1.4.1-win-x64.zip",
            "e66a4aacd00bcdedbe2564709afd1fbdc9751013034556e3ceb6fa39e55bb41e *ErdtreeKeeper.exe",
            "мусор",
            "");

        var sums = AppUpdate.ParseChecksums(text);

        Assert.Equal(2, sums.Count);
        Assert.Equal("4b795f8c6b6aa060ba951edb8aa234ae467e66b6912b82d727596fc12e5ac562",
            sums["ErdtreeKeeper-v1.4.1-win-x64.zip"]);
        Assert.Equal("e66a4aacd00bcdedbe2564709afd1fbdc9751013034556e3ceb6fa39e55bb41e",
            sums["ErdtreeKeeper.exe"]);
    }

    /// <summary>
    /// Строка, где вместо суммы что угодно, не должна превратиться в
    /// "ожидаемую сумму": иначе проверка совпадения потеряет смысл.
    /// </summary>
    [Theory]
    [InlineData("короткая  файл.zip")]
    [InlineData("zzzz5f8c6b6aa060ba951edb8aa234ae467e66b6912b82d727596fc12e5ac562  файл.zip")]
    [InlineData("4b795f8c6b6aa060ba951edb8aa234ae467e66b6912b82d727596fc12e5ac562")]
    public void Nonsense_lines_are_skipped(string line) =>
        Assert.Empty(AppUpdate.ParseChecksums(line));

    /// <summary>Адрес проверки записан в коде и никем не задаётся - это часть обещания.</summary>
    [Fact]
    public void The_check_address_is_fixed()
    {
        Assert.Equal(
            "https://api.github.com/repos/valiDol42/erdtree-keeper/releases/latest",
            AppUpdate.LatestReleaseUrl);

        Assert.True(AppUpdate.IsTrustedUrl(AppUpdate.LatestReleaseUrl));
        Assert.True(AppUpdate.IsTrustedUrl(AppUpdate.ReleasesPageUrl));
    }

    private static string Json() => """
        {
          "tag_name": "v1.5.0",
          "name": "1.5.0",
          "body": "Что нового: поддержка Dark Souls",
          "html_url": "https://github.com/valiDol42/erdtree-keeper/releases/tag/v1.5.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-09-19T10:00:00Z",
          "assets": [
            {
              "name": "ErdtreeKeeper-v1.5.0-win-x64.zip",
              "browser_download_url": "https://github.com/valiDol42/erdtree-keeper/releases/download/v1.5.0/ErdtreeKeeper-v1.5.0-win-x64.zip",
              "size": 34567890
            },
            {
              "name": "SHA256SUMS.txt",
              "browser_download_url": "https://github.com/valiDol42/erdtree-keeper/releases/download/v1.5.0/SHA256SUMS.txt",
              "size": 180
            }
          ]
        }
        """;
}
