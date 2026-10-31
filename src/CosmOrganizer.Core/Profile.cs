// CosmOrganizer.Core - Profile and Rule models
// Profiles group sources, destination, rules, filters and schedule.
// Rules have priority over categories.
// © CosmoNet.info — MIT License

using System.Text.Json.Serialization;

namespace CosmOrganizer.Core;

// ---------------------------------------------------------------------------
// Enumerations
// ---------------------------------------------------------------------------

/// <summary>How files are organized into subfolders.</summary>
public enum OrganizeMode
{
    ByCategory,          // categoria/
    ByCategoryYearMonth, // categoria/anno/mese/
    ByCreationDate,      // AAAA-MM-GG/
    ByModificationDate,  // AAAA-MM-GG (data modifica)
    ByExtension,         // .ext/
    ByFirstLetter,       // A/
    BySize               // Piccoli|Medi|Grandi/
}

/// <summary>Logical operator between rule conditions.</summary>
public enum ConditionLogic { And, Or }

/// <summary>How a name condition is matched.</summary>
public enum NameMatchType { Contains, StartsWith, EndsWith, Regex }

/// <summary>Age basis for age-based conditions.</summary>
public enum AgeBasis { Created, Modified }

/// <summary>Action performed when a rule matches.</summary>
public enum RuleAction { Move, Copy, Rename, Recycle, Compress, Ignore }

/// <summary>When the schedule fires.</summary>
public enum ScheduleTrigger { Manual, EveryNHours, DailyAt, OnStartup, FolderThreshold }

// ---------------------------------------------------------------------------
// Rule Condition
// ---------------------------------------------------------------------------

/// <summary>
/// One condition within a custom rule.
/// All fields are optional; only the populated ones are evaluated.
/// </summary>
public class RuleCondition
{
    // Name-based
    [JsonPropertyName("nameMatch")]
    public NameMatchType? NameMatch { get; set; }

    [JsonPropertyName("nameValue")]
    public string? NameValue { get; set; }

    // Extension list: ".pdf", ".doc", ...
    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }

    // Size thresholds (bytes)
    [JsonPropertyName("minSizeBytes")]
    public long? MinSizeBytes { get; set; }

    [JsonPropertyName("maxSizeBytes")]
    public long? MaxSizeBytes { get; set; }

    // Age
    [JsonPropertyName("ageBasis")]
    public AgeBasis? AgeBasis { get; set; }

    [JsonPropertyName("olderThanDays")]
    public int? OlderThanDays { get; set; }

    // Source folder filter
    [JsonPropertyName("sourceFolder")]
    public string? SourceFolder { get; set; }

    // Zone.Identifier (web download origin) — HostUrl or ReferrerUrl substring
    [JsonPropertyName("downloadSourceContains")]
    public string? DownloadSourceContains { get; set; }
}

// ---------------------------------------------------------------------------
// Custom Rule
// ---------------------------------------------------------------------------

/// <summary>
/// A user-defined rule with conditions and an action.
/// Rules are evaluated in order; the first match wins (unless all use ConditionLogic.Or).
/// </summary>
public class CustomRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("logic")]
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;

    [JsonPropertyName("conditions")]
    public List<RuleCondition> Conditions { get; set; } = new();

    [JsonPropertyName("action")]
    public RuleAction Action { get; set; } = RuleAction.Move;

    /// <summary>
    /// Destination path template. Supports placeholders:
    /// {categoria} {ext} {anno} {mese} {giorno} {nome} {dominio} {contatore}
    /// </summary>
    [JsonPropertyName("destinationTemplate")]
    public string DestinationTemplate { get; set; } = "{categoria}";

    /// <summary>Rename pattern (used when Action = Rename).</summary>
    [JsonPropertyName("renameTemplate")]
    public string? RenameTemplate { get; set; }

    /// <summary>Display order (lower = evaluated first).</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

// ---------------------------------------------------------------------------
// Filter
// ---------------------------------------------------------------------------

/// <summary>Filters applied before any rule or category logic.</summary>
public class ProfileFilter
{
    [JsonPropertyName("excludedExtensions")]
    public List<string> ExcludedExtensions { get; set; } = new();

    [JsonPropertyName("excludedFolderPatterns")]
    public List<string> ExcludedFolderPatterns { get; set; } = new();

    [JsonPropertyName("excludedGlobPatterns")]
    public List<string> ExcludedGlobPatterns { get; set; } = new();

    [JsonPropertyName("minSizeBytes")]
    public long MinSizeBytes { get; set; } = 0;

    [JsonPropertyName("maxSizeBytes")]
    public long MaxSizeBytes { get; set; } = long.MaxValue;

    /// <summary>Skip files modified less than N minutes ago (0 = no limit).</summary>
    [JsonPropertyName("skipNewerThanMinutes")]
    public int SkipNewerThanMinutes { get; set; } = 0;
}

// ---------------------------------------------------------------------------
// Schedule
// ---------------------------------------------------------------------------

/// <summary>When and how a profile runs automatically.</summary>
public class ProfileSchedule
{
    [JsonPropertyName("trigger")]
    public ScheduleTrigger Trigger { get; set; } = ScheduleTrigger.Manual;

    [JsonPropertyName("intervalHours")]
    public int IntervalHours { get; set; } = 6;

    [JsonPropertyName("dailyAt")]
    public TimeSpan DailyAt { get; set; } = TimeSpan.FromHours(9);

    /// <summary>Run when folder contains more than N files.</summary>
    [JsonPropertyName("thresholdFiles")]
    public int ThresholdFiles { get; set; } = 0;

    /// <summary>Run when folder exceeds N MB.</summary>
    [JsonPropertyName("thresholdMB")]
    public long ThresholdMB { get; set; } = 0;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
}

// ---------------------------------------------------------------------------
// Profile
// ---------------------------------------------------------------------------

/// <summary>
/// A named collection of sources, destination, rules, filters and schedule.
/// </summary>
public class Profile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Source folder paths.</summary>
    [JsonPropertyName("sourceFolders")]
    public List<string> SourceFolders { get; set; } = new();

    /// <summary>
    /// Destination base folder. Empty string = "same folder as source, in subfolders"
    /// (default, avoids destination-inside-source problems).
    /// </summary>
    [JsonPropertyName("destinationFolder")]
    public string DestinationFolder { get; set; } = string.Empty;

    [JsonPropertyName("organizeMode")]
    public OrganizeMode OrganizeMode { get; set; } = OrganizeMode.ByCategory;

    [JsonPropertyName("includeSubfolders")]
    public bool IncludeSubfolders { get; set; } = false;

    [JsonPropertyName("copyInsteadOfMove")]
    public bool CopyInsteadOfMove { get; set; } = false;

    [JsonPropertyName("deleteEmptyFolders")]
    public bool DeleteEmptyFolders { get; set; } = true;

    [JsonPropertyName("compressOldFiles")]
    public bool CompressOldFiles { get; set; } = false;

    [JsonPropertyName("compressOlderThanDays")]
    public int CompressOlderThanDays { get; set; } = 90;

    [JsonPropertyName("deleteDuplicates")]
    public bool DeleteDuplicates { get; set; } = true;

    [JsonPropertyName("realTimeEnabled")]
    public bool RealTimeEnabled { get; set; } = false;

    [JsonPropertyName("filter")]
    public ProfileFilter Filter { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<CustomRule> Rules { get; set; } = new();

    [JsonPropertyName("schedule")]
    public ProfileSchedule Schedule { get; set; } = new();
}
