using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceMite.Core;

public static class ConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        List<string> errors = [];
        if (settings.SchemaVersion != 1) errors.Add("ConfigVersion");
        if (settings.UiLanguage is not ("zh-CN" or "en")) errors.Add("ConfigLanguage");
        if (settings.SpeechLanguage is null || settings.SpeechLanguage.Length > 20 || settings.SpeechLanguage.Any(char.IsControl)) errors.Add("ConfigSpeechLanguage");
        if (string.IsNullOrWhiteSpace(settings.OutputLanguage) || settings.OutputLanguage.Length > 40 || settings.OutputLanguage.Any(char.IsControl)) errors.Add("ConfigOutputLanguage");
        if (!Enum.IsDefined(settings.Mode)) errors.Add("ConfigMode");
        if (!double.IsFinite(settings.FloatLeft) || !double.IsFinite(settings.FloatTop)) errors.Add("ConfigPosition");
        errors.AddRange(ValidateService(settings.Stt));
        if (settings.Llm is null) errors.Add("ConfigService");
        else if (!settings.TranscribeOnly) errors.AddRange(ValidateService(settings.Llm));
        return errors.Distinct().ToArray();
    }

    public static IReadOnlyList<string> ValidateService(ServiceConfig? config)
    {
        List<string> errors = [];
        if (config is null) return ["ConfigService"];
        if (!Enum.IsDefined(config.Kind)) errors.Add("ConfigService");
        if (config.TimeoutSeconds is < 5 or > 120) errors.Add("ConfigTimeout");
        if (config.Kind == ServiceKind.Demo) return errors;
        if (string.IsNullOrWhiteSpace(config.Model) || config.Model.Length > 120 || config.Model.Any(char.IsControl)) errors.Add("ConfigModel");
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) errors.Add("ConfigHttps");
        return errors;
    }
}

public sealed record LoadResult<T>(T Value, string? WarningCode = null);

/// <summary>Stores non-secret preferences only. API credentials belong in the Windows credential vault.</summary>
public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    private string PreferencesPath => Path.Combine(DirectoryPath, "preferences.json");

    public LoadResult<AppSettings> LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new(new());
            if (new FileInfo(SettingsPath).Length > 64 * 1024) return new(new(), "ConfigCorrupt");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            return settings is null || ConfigurationValidator.Validate(settings).Count > 0
                ? new(new(), "ConfigCorrupt") : new(settings);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return new(new(), "ConfigCorrupt"); }
    }

    public void SaveSettings(AppSettings settings)
    {
        if (ConfigurationValidator.Validate(settings).Count > 0) throw new ServiceException(ServiceErrorCode.InvalidConfiguration);
        WriteAtomic(SettingsPath, settings);
    }

    public LoadResult<LanguagePreferences> LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return new(new());
            if (new FileInfo(PreferencesPath).Length > 8192) return new(new(), "PreferencesCorrupt");
            var prefs = JsonSerializer.Deserialize<LanguagePreferences>(File.ReadAllText(PreferencesPath), JsonOptions);
            return prefs is null || !ValidPreferences(prefs) ? new(new(), "PreferencesCorrupt") : new(prefs);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return new(new(), "PreferencesCorrupt"); }
    }

    public void SavePreferences(LanguagePreferences preferences)
    {
        if (!ValidPreferences(preferences)) throw new ServiceException(ServiceErrorCode.InputTooLong);
        WriteAtomic(PreferencesPath, preferences);
    }

    public void DeletePreferences()
    {
        if (File.Exists(PreferencesPath)) File.Delete(PreferencesPath);
    }

    // Explicitly called only after the user confirms text in this tool; never retains words or a text history.
    public LanguagePreferences LearnFromConfirmedText(string confirmedText, LanguagePreferences previous)
    {
        if (confirmedText.Length is < 3 or > 16000) return previous;
        var learned = previous with
        {
            Punctuation = confirmedText.IndexOfAny(['，', '。', '！', '？', '；']) >= 0 ? "Chinese punctuation" : previous.Punctuation,
            Length = confirmedText.Length < 80 ? "Prefer concise expression" : previous.Length
        };
        SavePreferences(learned);
        return learned;
    }

    private static bool ValidPreferences(LanguagePreferences p) => new[] { p.Script, p.Punctuation, p.Length, p.Tone, p.Terms }
        .All(x => x is not null && x.Length <= 300 && !x.Contains('\0'));

    private void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
