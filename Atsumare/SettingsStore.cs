using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Atsumare;

public static class SettingsStore
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static AtsumareSettings Current { get; private set; } = new AtsumareSettings();

    public static event EventHandler? SettingsChanged;

    private const int CurrentSettingsMigrationVersion = 2;

    internal static string GetSettingsPath()
    {
        var overriddenPath = E2ETestMode.GetSettingsPathOverride();
        if (!string.IsNullOrWhiteSpace(overriddenPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(overriddenPath)!);
            return overriddenPath;
        }

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atsumare"
        );
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "settings.json");
    }

    internal static string? TryLoadUiLanguageOverride()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("UiLanguage", out var property))
                return null;

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<AtsumareSettings> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                Current = E2ETestMode.CreateDefaultSettings();
                return Current;
            }

            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize(
                json,
                SettingsJsonContext.Default.AtsumareSettings);

            Current = loaded ?? new AtsumareSettings();
            if (ApplyMigrations(Current))
            {
                var migratedJson = JsonSerializer.Serialize(
                    Current,
                    SettingsJsonContext.Default.AtsumareSettings);
                await File.WriteAllTextAsync(path, migratedJson);
            }
            return Current;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "SettingsStore.LoadAsync");
            Current = E2ETestMode.CreateDefaultSettings();
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task SaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var path = GetSettingsPath();
            var json = JsonSerializer.Serialize(
                Current,
                SettingsJsonContext.Default.AtsumareSettings);

            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "SettingsStore.SaveAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool ApplyMigrations(AtsumareSettings settings)
    {
        var changed = false;

        if (settings.SettingsMigrationVersion < 1)
        {
            settings.PreserveMaximizedOnMove = true;
            settings.FocusMovedAppAfterMove = true;
            settings.SettingsMigrationVersion = 1;
            changed = true;
        }

        if (settings.SettingsMigrationVersion < 2)
        {
            settings.ShowWindowCountInList = true;
            settings.SettingsMigrationVersion = CurrentSettingsMigrationVersion;
            changed = true;
        }

        return changed;
    }
}
