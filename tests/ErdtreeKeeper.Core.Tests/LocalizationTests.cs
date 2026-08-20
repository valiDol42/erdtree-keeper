using System.Text.RegularExpressions;
using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Перевод - такой же продукт, как и код: недостающая строка или потерянная
/// подстановка видны пользователю сразу. Здесь проверяется, что английская
/// половина таблицы существует целиком и совпадает с русской по смыслу
/// подстановок.
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly Lang _saved = Loc.Current.Language;

    public void Dispose() => Loc.Current.Language = _saved;

    [Fact]
    public void EveryKeyHasBothLanguages()
    {
        var empty = Loc.Keys
            .Where(k => string.IsNullOrWhiteSpace(Loc.Pair(k).Ru)
                     || string.IsNullOrWhiteSpace(Loc.Pair(k).En))
            .ToList();

        Assert.True(empty.Count == 0, "пустой перевод: " + string.Join(", ", empty));
    }

    /// <summary>
    /// Английская строка с кириллицей - это забытый перевод: скопировали
    /// русскую и не тронули.
    /// </summary>
    [Fact]
    public void EnglishHasNoCyrillic()
    {
        var cyrillic = Loc.Keys
            .Where(k => Loc.Pair(k).En.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё'))
            .ToList();

        Assert.True(cyrillic.Count == 0, "кириллица в переводе: " + string.Join(", ", cyrillic));
    }

    /// <summary>
    /// Подстановки нумерованные, и порядок аргументов один на оба языка.
    /// Лишний {2} в переводе - исключение при показе строки.
    /// </summary>
    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var mismatched = new List<string>();

        foreach (var key in Loc.Keys)
        {
            var (ru, en) = Loc.Pair(key);
            if (Placeholders(ru).SetEquals(Placeholders(en))) continue;
            mismatched.Add(key);
        }

        Assert.True(mismatched.Count == 0, "подстановки разошлись: " + string.Join(", ", mismatched));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    /// <summary>Формы множественного числа: три в русском, две в английском.</summary>
    [Fact]
    public void PluralFormsHaveThreeVariants()
    {
        var broken = Loc.Keys
            .Where(k => k.StartsWith("plural.", StringComparison.Ordinal))
            .Where(k => Loc.Pair(k).Ru.Split('|').Length != 3 || Loc.Pair(k).En.Split('|').Length != 3)
            .ToList();

        Assert.True(broken.Count == 0, "не три формы: " + string.Join(", ", broken));
        Assert.NotEmpty(Loc.Keys.Where(k => k.StartsWith("plural.", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(1, "снимок")]
    [InlineData(2, "снимка")]
    [InlineData(4, "снимка")]
    [InlineData(5, "снимков")]
    // 11-14 - исключение: по последней цифре они просились бы в первую форму.
    [InlineData(11, "снимков")]
    [InlineData(12, "снимков")]
    [InlineData(21, "снимок")]
    [InlineData(22, "снимка")]
    [InlineData(105, "снимков")]
    public void RussianPluralPicksRightForm(int count, string expected)
    {
        Loc.Current.Language = Lang.Ru;
        Assert.Equal(expected, Loc.Plural(count, "plural.snapshot"));
    }

    [Theory]
    [InlineData(1, "snapshot")]
    [InlineData(2, "snapshots")]
    [InlineData(11, "snapshots")]
    public void EnglishPluralPicksRightForm(int count, string expected)
    {
        Loc.Current.Language = Lang.En;
        Assert.Equal(expected, Loc.Plural(count, "plural.snapshot"));
    }

    [Fact]
    public void UnknownKeyComesBackAsItself()
    {
        Assert.Equal("нет.такого.ключа", Loc.Get("нет.такого.ключа"));
    }

    [Fact]
    public void SwitchingLanguageChangesStrings()
    {
        Loc.Current.Language = Lang.Ru;
        var ru = Loc.Get("app.close");

        Loc.Current.Language = Lang.En;
        var en = Loc.Get("app.close");

        Assert.NotEqual(ru, en);
        Assert.Equal(Loc.Pair("app.close").En, en);
    }

    /// <summary>
    /// Разметка обновляется по уведомлению об индексаторе - без него
    /// переключатель языка меняет настройку, но не окно.
    /// </summary>
    [Fact]
    public void SwitchingLanguageRaisesIndexerNotification()
    {
        Loc.Current.Language = Lang.Ru;

        var raised = new List<string?>();
        void Handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            raised.Add(e.PropertyName);

        Loc.Current.PropertyChanged += Handler;
        try
        {
            Loc.Current.Language = Lang.En;
        }
        finally
        {
            Loc.Current.PropertyChanged -= Handler;
        }

        Assert.Contains("Item[]", raised);
        Assert.Contains("IsEnglish", raised);
    }

    /// <summary>
    /// Сборка идёт без ICU, поэтому язык системы читается у Windows, а не у
    /// CultureInfo. Проверяются обе таблицы соответствия.
    /// </summary>
    [Theory]
    [InlineData(0x0419, Lang.Ru)]   // ru-RU
    [InlineData(0x0422, Lang.Ru)]   // uk-UA
    [InlineData(0x0423, Lang.Ru)]   // be-BY
    [InlineData(0x043F, Lang.Ru)]   // kk-KZ
    [InlineData(0x0409, Lang.En)]   // en-US
    [InlineData(0x0809, Lang.En)]   // en-GB
    [InlineData(0x0407, Lang.En)]   // de-DE
    [InlineData(0x0411, Lang.En)]   // ja-JP
    public void WindowsLanguageIdDecidesDefault(int langId, Lang expected)
    {
        Assert.Equal(expected, Loc.FromWindowsLanguageId(langId));
    }

    [Theory]
    [InlineData("ru", Lang.Ru)]
    [InlineData("uk", Lang.Ru)]
    [InlineData("be", Lang.Ru)]
    [InlineData("kk", Lang.Ru)]
    [InlineData("en", Lang.En)]
    [InlineData("de", Lang.En)]
    [InlineData("ja", Lang.En)]
    [InlineData("iv", Lang.En)]
    public void CultureNameDecidesDefault(string name, Lang expected)
    {
        Assert.Equal(expected, Loc.FromCultureName(name));
    }

    [Fact]
    public void DetectingSystemLanguageDoesNotThrow()
    {
        var detected = Loc.DetectFromSystem();
        Assert.True(detected is Lang.Ru or Lang.En);
    }
}
