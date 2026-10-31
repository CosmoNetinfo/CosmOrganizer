// CosmOrganizer.Core - Settings model and SettingsService
// Atomic read/write, migration from v3 JSON, corrupt-file recovery.
// © CosmoNet.info — MIT License

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CosmOrganizer.Core;

// ---------------------------------------------------------------------------
// Settings model
// ---------------------------------------------------------------------------

/// <summary>
/// Application settings persisted to %APPDATA%\CosmOrganizer\settings.json.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("autoUpdateEnabled")]
    public bool AutoUpdateEnabled { get; set; } = true;

    [JsonPropertyName("deleteEmptyFolders")]
    public bool DeleteEmptyFolders { get; set; } = true;

    [JsonPropertyName("compressOldFiles")]
    public bool CompressOldFiles { get; set; } = false;

    [JsonPropertyName("includeSubfolders")]
    public bool IncludeSubfolders { get; set; } = true;

    [JsonPropertyName("sourceFolders")]
    public List<string> SourceFolders { get; set; } = new();

    [JsonPropertyName("lastDestinationFolder")]
    public string LastDestinationFolder { get; set; } = string.Empty;

    [JsonPropertyName("deleteDuplicates")]
    public bool DeleteDuplicates { get; set; } = true; // true = Recycle Bin, false = keep

    [JsonPropertyName("windowLeft")]
    public int WindowLeft { get; set; } = 0;

    [JsonPropertyName("windowTop")]
    public int WindowTop { get; set; } = 0;

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 0;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 0;

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; } = false;
}

// ---------------------------------------------------------------------------
// SettingsService
// ---------------------------------------------------------------------------

/// <summary>
/// Loads and saves <see cref="AppSettings"/> with atomic writes and migration support.
/// </summary>
public class SettingsService
{
    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(string? settingsDir = null, string? legacySettingsPath = null)
    {
        _settingsDir = settingsDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer");
        _settingsPath = Path.Combine(_settingsDir, "settings.json");
        _legacySettingsPath = legacySettingsPath ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer_v3.json");
    }

    /// <summary>
    /// Loads settings. Returns (settings, wasCorrupted).
    /// - If new file missing: tries to migrate from legacy v3 file, else returns defaults.
    /// - If corrupt: backs up to .bak and returns defaults with wasCorrupted = true.
    /// </summary>
    public (AppSettings Settings, bool WasCorrupted) Load()
    {
        Directory.CreateDirectory(_settingsDir);

        if (!File.Exists(_settingsPath))
        {
            // Attempt migration from legacy v3 file
            if (File.Exists(_legacySettingsPath))
            {
                var migrated = TryMigrateFromV3();
                if (migrated != null)
                {
                    Save(migrated);
                    return (migrated, false);
                }
            }
            return (new AppSettings(), false);
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (settings == null) throw new JsonException("Null result");
            return (settings, false);
        }
        catch
        {
            // Back up corrupt file
            var bakPath = _settingsPath + ".bak";
            try { File.Copy(_settingsPath, bakPath, overwrite: true); } catch { /* ignore */ }

            return (new AppSettings(), true);
        }
    }

    /// <summary>
    /// Saves settings atomically: writes to a temp file, then replaces the target.
    /// </summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_settingsDir);

        var json = JsonSerializer.Serialize(settings, JsonOpts);
        var tempPath = _settingsPath + ".tmp";

        File.WriteAllText(tempPath, json);

        if (File.Exists(_settingsPath))
            File.Replace(tempPath, _settingsPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, _settingsPath);
    }

    // ---------------------------------------------------------------------------
    // Legacy migration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Attempts to parse the old v3 JSON file and map known keys to <see cref="AppSettings"/>.
    /// Returns null if the file cannot be read or parsed.
    /// </summary>
    private AppSettings? TryMigrateFromV3()
    {
        try
        {
            var json = File.ReadAllText(_legacySettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var s = new AppSettings();

            if (root.TryGetProperty("deleteEmptyFolders", out var def))
                s.DeleteEmptyFolders = def.GetBoolean();

            if (root.TryGetProperty("compressOldFiles", out var cof))
                s.CompressOldFiles = cof.GetBoolean();

            if (root.TryGetProperty("includeSubfolders", out var isf))
                s.IncludeSubfolders = isf.GetBoolean();

            if (root.TryGetProperty("sourceFolders", out var sf) &&
                sf.ValueKind == JsonValueKind.Array)
            {
                s.SourceFolders = sf.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            }

            if (root.TryGetProperty("lastDestinationFolder", out var ldf) &&
                ldf.ValueKind == JsonValueKind.String)
                s.LastDestinationFolder = ldf.GetString() ?? string.Empty;

            if (root.TryGetProperty("deleteDuplicates", out var dd))
                s.DeleteDuplicates = dd.GetBoolean();

            return s;
        }
        catch
        {
            return null;
        }
    }
}
