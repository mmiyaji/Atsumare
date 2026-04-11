using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Windows.Globalization;
using Windows.System.UserProfile;

namespace Atsumare;

public static class AppLanguage
{
    internal const string System = "";
    internal const string English = "en-US";
    internal const string Japanese = "ja-JP";

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return System;

        return value.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" => English,
            "ja" or "ja-jp" => Japanese,
            _ => System
        };
    }

    internal static string GetEffectiveLanguage(AtsumareSettings settings)
    {
        var preferred = Normalize(settings.UiLanguage);
        if (!string.IsNullOrEmpty(preferred))
            return preferred;

        return PrefersJapanese() ? Japanese : English;
    }

    internal static void Apply(AtsumareSettings settings)
    {
        var effective = GetEffectiveLanguage(settings);
        ApplicationLanguages.PrimaryLanguageOverride = effective;

        var culture = CultureInfo.GetCultureInfo(effective);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    internal static void ApplyStartupOverride(string? configuredLanguage)
    {
        var normalized = Normalize(configuredLanguage);
        var effective = string.IsNullOrEmpty(normalized)
            ? (PrefersJapanese() ? Japanese : English)
            : normalized;

        ApplicationLanguages.PrimaryLanguageOverride = effective;

        var culture = CultureInfo.GetCultureInfo(effective);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private static bool PrefersJapanese()
    {
        var language = GlobalizationPreferences.Languages.FirstOrDefault()
            ?? ApplicationLanguages.Languages.FirstOrDefault()
            ?? CultureInfo.CurrentUICulture.Name;
        return language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    }
}
