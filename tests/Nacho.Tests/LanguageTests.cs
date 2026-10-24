using System.Text.RegularExpressions;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

/// <summary>Every UI language must have every key, the same placeholders, and its own number format.</summary>
public sealed class LanguageTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}");

    [Theory]
    [InlineData("es")]
    [InlineData("hu")]
    public void Every_language_has_exactly_the_English_keys(string lang)
    {
        var dict = Strings.For(lang);
        Assert.Empty(Strings.En.Keys.Except(dict.Keys));
        Assert.Empty(dict.Keys.Except(Strings.En.Keys));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("hu")]
    public void Placeholders_match_English(string lang)
    {
        var dict = Strings.For(lang);
        foreach (var (key, en) in Strings.En)
        {
            var a = Placeholder.Matches(en).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = Placeholder.Matches(dict[key]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), $"{lang}: {key}");
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("hu")]
    public void Every_text_formats_without_errors(string lang)
    {
        var culture = Strings.CultureFor(lang);
        var args = new object[] { "a", "b", "c", "d" };
        foreach (var (key, text) in Strings.For(lang))
        {
            string s = string.Format(culture, text, args);
            Assert.False(string.IsNullOrWhiteSpace(s), key);
        }
    }

    [Fact]
    public void No_long_dashes_in_Hungarian()
    {
        foreach (var s in Strings.Hu.Values)
        {
            Assert.DoesNotContain('—', s);
            Assert.DoesNotContain('–', s);
        }
    }

    [Fact]
    public void Hungarian_is_a_real_translation()
    {
        int same = Strings.En.Count(kv => Strings.Hu[kv.Key] == kv.Value);
        // Only codes and units may stay the same (A4, Letter, "{0} · {1} px").
        Assert.True(same <= 3, $"{same} Hungarian texts are identical to English");
        Assert.Equal("Normalizál, Arányt tart, Csökkent és Hűen Odaad", Strings.Hu["App.Expansion"]);
    }

    [Theory]
    [InlineData("en", "en-IE")]
    [InlineData("es", "es-ES")]
    [InlineData("hu", "hu-HU")]
    [InlineData("de", "en-IE")]
    [InlineData("", "en-IE")]
    public void Languages_map_to_their_culture(string lang, string culture)
    {
        Assert.Equal(culture, Strings.CultureFor(lang).Name);
    }

    [Fact]
    public void Hungarian_numbers_use_a_decimal_comma()
    {
        var hu = Strings.CultureFor("hu");
        Assert.Equal("4,8 MB", Strings.Bytes(5_033_165, hu));
        Assert.Equal("236 KB", Strings.Bytes(241_664, hu));
        Assert.Equal("1,82:1", Shape.Label(2000, 1100, hu));
        Assert.Equal("97%-kal kisebb", string.Format(hu, Strings.Hu["Tile.Smaller"], 97));
    }

    [Fact]
    public void Languages_list_matches_the_dictionaries()
    {
        Assert.Equal(new[] { "en", "es", "hu" }, Strings.Languages);
        Assert.Same(Strings.Hu, Strings.For("hu"));
        Assert.Same(Strings.En, Strings.For("xx"));
    }
}
