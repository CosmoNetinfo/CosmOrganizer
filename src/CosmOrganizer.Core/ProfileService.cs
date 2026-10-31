// CosmOrganizer.Core - ProfileService
// CRUD for profiles stored in %APPDATA%\CosmOrganizer\profiles.json.
// Also provides Known Folder path resolution (Downloads can be on another drive).
// © CosmoNet.info — MIT License

using System.Runtime.InteropServices;
using System.Text.Json;

namespace CosmOrganizer.Core;

/// <summary>
/// Loads, saves and provides default profiles.
/// Profiles are persisted to %APPDATA%\CosmOrganizer\profiles.json.
/// </summary>
public class ProfileService
{
    // ---------------------------------------------------------------------------
    // Known Folder GUIDs (Windows Shell)
    // ---------------------------------------------------------------------------
    private static readonly Guid FOLDERID_Downloads  = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid FOLDERID_Desktop     = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid FOLDERID_Documents   = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid FOLDERID_Pictures    = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    private static readonly Guid FOLDERID_Videos      = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");
    private static readonly Guid FOLDERID_Music       = new("4BD8D571-6D19-48D3-BE97-422220080E43");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    // ---------------------------------------------------------------------------
    // Storage path
    // ---------------------------------------------------------------------------
    private static readonly string ProfilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer");
    private static readonly string ProfilesPath =
        Path.Combine(ProfilesDir, "profiles.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>Loads all profiles. Creates defaults if the file is missing.</summary>
    public List<Profile> Load()
    {
        Directory.CreateDirectory(ProfilesDir);

        if (!File.Exists(ProfilesPath))
        {
            var defaults = CreateDefaultProfiles();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var profiles = JsonSerializer.Deserialize<List<Profile>>(json, JsonOpts);
            return profiles ?? CreateDefaultProfiles();
        }
        catch
        {
            // Backup corrupt file
            try { File.Copy(ProfilesPath, ProfilesPath + ".bak", overwrite: true); } catch { }
            return CreateDefaultProfiles();
        }
    }

    /// <summary>Saves all profiles atomically.</summary>
    public void Save(List<Profile> profiles)
    {
        Directory.CreateDirectory(ProfilesDir);
        var json = JsonSerializer.Serialize(profiles, JsonOpts);
        var tmp = ProfilesPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(ProfilesPath))
            File.Replace(tmp, ProfilesPath, destinationBackupFileName: null);
        else
            File.Move(tmp, ProfilesPath);
    }

    // ---------------------------------------------------------------------------
    // Known Folders
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the real path of a Windows Known Folder (e.g. Downloads on M:).
    /// Falls back to the standard Environment.SpecialFolder equivalent.
    /// </summary>
    public static string GetKnownFolderPath(Guid folderId)
    {
        try
        {
            int result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out IntPtr pPath);
            if (result == 0)
            {
                var path = Marshal.PtrToStringUni(pPath)!;
                Marshal.FreeCoTaskMem(pPath);
                return path;
            }
        }
        catch { }
        return string.Empty;
    }

    public static string GetDownloadsPath()
        => GetKnownFolderPath(FOLDERID_Downloads);

    public static string GetDesktopPath()
        => GetKnownFolderPath(FOLDERID_Desktop).IfEmpty(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

    public static string GetDocumentsPath()
        => GetKnownFolderPath(FOLDERID_Documents).IfEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    public static string GetPicturesPath()
        => GetKnownFolderPath(FOLDERID_Pictures).IfEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    public static string GetVideosPath()
        => GetKnownFolderPath(FOLDERID_Videos).IfEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

    public static string GetMusicPath()
        => GetKnownFolderPath(FOLDERID_Music).IfEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

    // ---------------------------------------------------------------------------
    // Default profiles
    // ---------------------------------------------------------------------------

    private static List<Profile> CreateDefaultProfiles()
    {
        var downloads = GetDownloadsPath();
        var desktop   = GetDesktopPath();
        var documents = GetDocumentsPath();

        return new List<Profile>
        {
            new Profile
            {
                Name           = "Download",
                Enabled        = true,
                SourceFolders  = new() { downloads },
                // Default: same folder as source, in subfolders (dest empty = source)
                DestinationFolder = string.Empty,
                OrganizeMode   = OrganizeMode.ByCategory,
                IncludeSubfolders = false,
                DeleteEmptyFolders = true,
                DeleteDuplicates   = true,
                Rules = new()
                {
                    new CustomRule
                    {
                        Name  = "Fatture PDF",
                        Order = 0,
                        Logic = ConditionLogic.And,
                        Conditions = new()
                        {
                            new() { Extensions = new() { ".pdf" } },
                            new() { NameMatch = NameMatchType.Contains, NameValue = "fattura" }
                        },
                        Action              = RuleAction.Move,
                        DestinationTemplate = "Documenti\\Fatture"
                    },
                    new CustomRule
                    {
                        Name  = "Screenshot",
                        Order = 1,
                        Logic = ConditionLogic.Or,
                        Conditions = new()
                        {
                            new() { NameMatch = NameMatchType.StartsWith, NameValue = "Screenshot" },
                            new() { NameMatch = NameMatchType.StartsWith, NameValue = "Schermata" },
                            new() { NameMatch = NameMatchType.StartsWith, NameValue = "Snip" },
                            new() { NameMatch = NameMatchType.StartsWith, NameValue = "Cattura" }
                        },
                        Action              = RuleAction.Move,
                        DestinationTemplate = "Immagini\\Screenshot"
                    },
                    new CustomRule
                    {
                        Name  = "Installer",
                        Order = 2,
                        Logic = ConditionLogic.And,
                        Conditions = new()
                        {
                            new() { Extensions = new() { ".exe", ".msi", ".msix" } }
                        },
                        Action              = RuleAction.Move,
                        DestinationTemplate = "Installer"
                    }
                }
            },
            new Profile
            {
                Name           = "Desktop",
                Enabled        = false,
                SourceFolders  = new() { desktop },
                DestinationFolder = string.Empty,
                OrganizeMode   = OrganizeMode.ByCategory,
                IncludeSubfolders = false,
                // Desktop: do NOT delete .lnk shortcuts
                Filter = new ProfileFilter
                {
                    ExcludedExtensions = new() { ".lnk", ".url" }
                }
            },
            new Profile
            {
                Name           = "Documenti",
                Enabled        = false,
                SourceFolders  = new() { documents },
                DestinationFolder = string.Empty,
                OrganizeMode   = OrganizeMode.ByCategoryYearMonth,
                IncludeSubfolders = true
            }
        };
    }
}

// ---------------------------------------------------------------------------
// Helper extension
// ---------------------------------------------------------------------------
file static class StringExtensions
{
    public static string IfEmpty(this string s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s;
}
