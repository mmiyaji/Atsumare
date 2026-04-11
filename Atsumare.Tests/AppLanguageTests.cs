using Xunit;

namespace Atsumare.Tests;

public sealed class AppLanguageTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("en", "en-US")]
    [InlineData("en-US", "en-US")]
    [InlineData("ja", "ja-JP")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("fr-FR", "")]
    public void Normalize_MapsSupportedLanguageTags(string? value, string expected)
    {
        Assert.Equal(expected, AppLanguage.Normalize(value));
    }

    [Fact]
    public void GetEffectiveLanguage_UsesExplicitEnglish()
    {
        var settings = new AtsumareSettings { UiLanguage = "en-US" };
        Assert.Equal(AppLanguage.English, AppLanguage.GetEffectiveLanguage(settings));
    }

    [Fact]
    public void GetEffectiveLanguage_UsesExplicitJapanese()
    {
        var settings = new AtsumareSettings { UiLanguage = "ja-JP" };
        Assert.Equal(AppLanguage.Japanese, AppLanguage.GetEffectiveLanguage(settings));
    }
}
