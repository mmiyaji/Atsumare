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

    private static string GetSettingsPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atsumare"
        );
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "settings.json");
    }

    public static async Task<AtsumareSettings> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                Current = new AtsumareSettings();
                return Current;
            }

            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize(
                json,
                SettingsJsonContext.Default.AtsumareSettings);

            Current = loaded ?? new AtsumareSettings();
            return Current;
        }
        catch
        {
            Current = new AtsumareSettings();
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
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }
}