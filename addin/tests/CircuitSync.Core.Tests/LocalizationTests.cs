using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void Both_languages_cover_the_same_keys()
    {
        var missingInEnglish = Translations.Id.Keys.Except(Translations.En.Keys).OrderBy(k => k).ToList();
        var missingInIndonesian = Translations.En.Keys.Except(Translations.Id.Keys).OrderBy(k => k).ToList();

        Assert.Empty(missingInEnglish);
        Assert.Empty(missingInIndonesian);
    }

    [Fact]
    public void No_translation_is_blank()
    {
        Assert.All(Translations.Id, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(Translations.En, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    /// <summary>
    /// Placeholder yang tidak sama jumlahnya antar bahasa akan memunculkan teks mentah
    /// di UI, dan itu tidak kelihatan sampai bahasa diganti.
    /// </summary>
    [Fact]
    public void Placeholder_count_matches_between_languages()
    {
        foreach (var (key, indonesian) in Translations.Id)
        {
            var english = Translations.En[key];
            Assert.Equal(PlaceholderCount(indonesian), PlaceholderCount(english));
        }
    }

    [Fact]
    public void Every_device_status_has_a_label()
    {
        var translator = new Translator();
        foreach (var status in DeviceStatus.All)
        {
            Assert.NotEqual($"status.{status}", translator[$"status.{status}"]);
        }
    }

    [Fact]
    public void Every_plan_problem_has_a_label()
    {
        var translator = new Translator();
        string[] keys =
        [
            PlanValidator.EmptyCircuit, PlanValidator.UnknownPanel, PlanValidator.PanelNotUsable,
            PlanValidator.UnknownDevice, PlanValidator.DeviceWithoutConnector, PlanValidator.KindMismatch,
            PlanValidator.DeviceInTwoCircuits, PlanValidator.AlreadyConnected,
        ];

        Assert.All(keys, key => Assert.NotEqual(key, translator[key]));
    }

    [Fact]
    public void Unknown_key_comes_back_as_the_key_itself()
    {
        var translator = new Translator();
        Assert.Equal("tidak.ada", translator["tidak.ada"]);
    }

    [Fact]
    public void English_falls_back_to_indonesian_when_a_key_is_only_defined_there()
    {
        var translator = new Translator(Language.En) { Language = Language.En };
        Assert.Equal(Translations.En["app.title"], translator["app.title"]);
    }

    [Fact]
    public void Language_code_round_trips()
    {
        Assert.Equal(Language.En, Translator.ParseLanguage("EN"));
        Assert.Equal(Language.Id, Translator.ParseLanguage("id"));
        Assert.Equal(Language.Id, Translator.ParseLanguage(null));
        Assert.Equal("en", Translator.ToCode(Language.En));
        Assert.Equal("id", Translator.ToCode(Language.Id));
    }

    private static int PlaceholderCount(string text)
    {
        var count = 0;
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (text[index] == '{' && char.IsDigit(text[index + 1]))
            {
                count++;
            }
        }

        return count;
    }
}
