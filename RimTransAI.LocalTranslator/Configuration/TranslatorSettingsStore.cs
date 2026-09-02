using System.Text.Json;

namespace RimTransAI.LocalTranslator.Configuration;

public sealed class TranslatorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public TranslatorSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RimTransAI.LocalTranslator");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public TranslatorOptions Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new TranslatorOptions();
            return JsonSerializer.Deserialize<TranslatorOptions>(File.ReadAllText(_settingsPath), JsonOptions)
                ?? new TranslatorOptions();
        }
        catch (Exception)
        {
            return new TranslatorOptions();
        }
    }

    public void Save(TranslatorOptions options)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(options, JsonOptions));
    }
}
