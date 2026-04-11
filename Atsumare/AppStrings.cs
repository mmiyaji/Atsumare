using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Windows.ApplicationModel.Resources;

namespace Atsumare;

internal static class AppStrings
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static string Get(string key)
    {
        var language = AppLanguage.GetEffectiveLanguage(SettingsStore.Current);
        var value = GetFromResw(language, key) ?? GetFromResw(AppLanguage.English, key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        var loader = ResourceLoader.GetForViewIndependentUse();
        value = loader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    internal static string Format(string key, params object[] args)
    {
        var format = Get(key);
        return string.Format(CultureInfo.CurrentUICulture, format, args);
    }

    private static IReadOnlyDictionary<string, string> LoadResw(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var doc = XDocument.Load(path);
        return doc.Root?
            .Elements("data")
            .Select(x => new
            {
                Key = (string?)x.Attribute("name"),
                Value = (string?)x.Element("value")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key!, x => x.Value ?? "", StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetFromResw(string language, string key)
    {
        var map = Cache.GetOrAdd(language, LoadResw);
        return map.TryGetValue(key, out var value) ? value : null;
    }
}
