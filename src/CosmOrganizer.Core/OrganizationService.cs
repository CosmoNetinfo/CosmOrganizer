// CosmOrganizer.Core - OrganizationService
using System.IO.Compression;
// Orchestrates file organization for a Profile:
// applies filters, rules, category fallback, organize-mode path building,
// preview mode (dry-run), and journal recording.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

// ---------------------------------------------------------------------------
// Preview result (dry-run)
// ---------------------------------------------------------------------------

/// <summary>One row in a preview / dry-run table.</summary>
public class PreviewItem
{
    public string SourcePath    { get; init; } = string.Empty;
    public string DestPath      { get; init; } = string.Empty;
    public string RuleOrCategory { get; init; } = string.Empty;
    public RuleAction Action    { get; init; }
    public bool Excluded        { get; set;  }
}

// ---------------------------------------------------------------------------
// OrganizationService
// ---------------------------------------------------------------------------

/// <summary>
/// Runs file organization for a single <see cref="Profile"/>.
/// Thread-safe for progress reporting; must be called from a background task.
/// </summary>
public class OrganizationService
{
    private readonly SafeFileService  _safe;
    private readonly CategoryService  _categories;
    private readonly RuleEngine       _rules;
    private readonly JournalService   _journal;

    public OrganizationService(
        SafeFileService safe,
        CategoryService categories,
        RuleEngine rules,
        JournalService journal)
    {
        _safe       = safe;
        _categories = categories;
        _rules      = rules;
        _journal    = journal;
    }

    // ---------------------------------------------------------------------------
    // Preview (dry-run)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns a preview table without touching any files.
    /// </summary>
    public List<PreviewItem> Preview(Profile profile)
    {
        var files   = EnumerateFiles(profile);
        var results = new List<PreviewItem>();
        var counter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (ShouldSkip(file, profile, out _)) continue;
            BuildPreviewItem(file, profile, counter, results);
        }

        return results;
    }

    private void BuildPreviewItem(
        string file, Profile profile,
        Dictionary<string, int> counter,
        List<PreviewItem> results)
    {
        var ruleMatch = _rules.Evaluate(file, profile.Rules);
        string label, destPath;
        RuleAction action;

        if (ruleMatch?.Rule != null)
        {
            label   = $"Regola: {ruleMatch.Rule.Name}";
            action  = ruleMatch.Rule.Action;
            destPath = ResolveDestination(file, profile, ruleMatch.ResolvedDestination, counter, action);
        }
        else
        {
            var cat  = _categories.GetCategory(file);
            label    = $"Categoria: {cat}";
            action   = profile.CopyInsteadOfMove ? RuleAction.Copy : RuleAction.Move;
            destPath = ResolveDestination(file, profile, cat, counter, action);
        }

        results.Add(new PreviewItem
        {
            SourcePath      = file,
            DestPath        = destPath,
            RuleOrCategory  = label,
            Action          = action
        });
    }

    // ---------------------------------------------------------------------------
    // Run
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Organizes files according to <paramref name="profile"/>.
    /// Reports progress via <paramref name="progress"/> (0–100).
    /// </summary>
    public (int Moved, int Errors) Run(
        Profile profile,
        string runId,
        CancellationToken ct,
        IProgress<(int Percent, string File)>? progress = null)
    {
        var files   = EnumerateFiles(profile);
        int total   = files.Count;
        int moved   = 0, errors = 0;
        var counter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dirsEmptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < total && !ct.IsCancellationRequested; i++)
        {
            var file = files[i];
            progress?.Report(((int)((long)i * 100 / total), file));

            try
            {
                if (ShouldSkip(file, profile, out var reason))
                    continue;

                ProcessFile(file, profile, runId, counter, dirsEmptied);
                moved++;
            }
            catch (Exception ex)
            {
                errors++;
                // Per-file errors are non-fatal; caller may log them.
                System.Diagnostics.Debug.WriteLine($"[OrganizationService] Error on {file}: {ex.Message}");
            }
        }

        if (profile.DeleteEmptyFolders && !profile.CopyInsteadOfMove)
        {
            foreach (var src in profile.SourceFolders)
                _safe.DeleteEmptyDirs(src, dirsEmptied);
        }

        return (moved, errors);
    }

    // ---------------------------------------------------------------------------
    // Single-file processing
    // ---------------------------------------------------------------------------

    private void ProcessFile(
        string file, Profile profile, string runId,
        Dictionary<string, int> counter,
        HashSet<string> dirsEmptied)
    {
        // Safety guards
        if (_safe.IsProtectedPath(file)) return;

        var ruleMatch = _rules.Evaluate(file, profile.Rules);
        string relDest;
        RuleAction action;

        if (ruleMatch?.Rule != null)
        {
            action  = ruleMatch.Rule.Action;
            relDest = ruleMatch.ResolvedDestination;
        }
        else
        {
            relDest = _categories.GetCategory(file);
            action  = profile.CopyInsteadOfMove ? RuleAction.Copy : RuleAction.Move;
        }

        // Ignore rule: stop here
        if (action == RuleAction.Ignore) return;

        // Recycle rule
        if (action == RuleAction.Recycle)
        {
            _safe.RecycleFile(file);
            _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "recycled", file, string.Empty));
            dirsEmptied.Add(Path.GetDirectoryName(file)!);
            return;
        }

        var destPath = ResolveDestination(file, profile, relDest, counter, action);
        if (string.IsNullOrEmpty(destPath)) return;

        // Skip if same path
        if (_safe.IsSamePath(file, destPath)) return;

        // Skip if destination inside source
        foreach (var src in profile.SourceFolders)
            if (_safe.IsDestinationInsideSource(src, Path.GetDirectoryName(destPath)!)) return;

        // Handle collision
        destPath = MakeUniqueDestination(destPath);

        // Perform action
        switch (action)
        {
            case RuleAction.Move:
            case RuleAction.Compress when ShouldCompress(file, profile):
                bool compress = action == RuleAction.Compress ||
                    (profile.CompressOldFiles && ShouldCompress(file, profile));

                if (compress && !_safe.IsAlreadyCompressed(Path.GetExtension(file)))
                {
                    var zipPath = Path.ChangeExtension(destPath, ".zip");
                    zipPath = MakeUniqueDestination(zipPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                    using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                        zip.CreateEntryFromFile(file, Path.GetFileName(file));

                    if (!profile.CopyInsteadOfMove)
                    {
                        _safe.RecycleFile(file);
                        dirsEmptied.Add(Path.GetDirectoryName(file)!);
                    }
                    _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "compressed", file, zipPath));
                }
                else
                {
                    _safe.MoveFile(file, destPath);
                    dirsEmptied.Add(Path.GetDirectoryName(file)!);
                    _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "moved", file, destPath));
                }
                break;

            case RuleAction.Copy:
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, overwrite: false);
                _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "copied", file, destPath));
                break;

            case RuleAction.Rename:
                if (ruleMatch?.ResolvedName is { } newName && !string.IsNullOrEmpty(newName))
                {
                    var renamedPath = Path.Combine(Path.GetDirectoryName(file)!, newName + Path.GetExtension(file));
                    File.Move(file, MakeUniqueDestination(renamedPath));
                    _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "moved", file, renamedPath));
                }
                break;
        }
    }

    // ---------------------------------------------------------------------------
    // Destination path building
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds the full destination path for a file given a relative subfolder.
    /// When profile.DestinationFolder is empty, destination is inside the source folder.
    /// </summary>
    private string ResolveDestination(
        string filePath, Profile profile,
        string relativeSubfolder,
        Dictionary<string, int> counter,
        RuleAction action)
    {
        // Find which source folder the file belongs to
        var sourceRoot = profile.SourceFolders
            .FirstOrDefault(s => filePath.StartsWith(s, StringComparison.OrdinalIgnoreCase))
            ?? profile.SourceFolders.FirstOrDefault()
            ?? string.Empty;

        var baseDir = string.IsNullOrEmpty(profile.DestinationFolder)
            ? sourceRoot
            : profile.DestinationFolder;

        if (string.IsNullOrEmpty(baseDir)) return string.Empty;

        // Add organize-mode subdirectory
        var modeSubdir = BuildModeSubdir(filePath, profile.OrganizeMode);

        // Combine: base / relativeSubfolder / modeSubdir / filename
        var destDir = string.IsNullOrEmpty(modeSubdir)
            ? Path.Combine(baseDir, relativeSubfolder)
            : Path.Combine(baseDir, relativeSubfolder, modeSubdir);

        return Path.Combine(destDir, Path.GetFileName(filePath));
    }

    private static string BuildModeSubdir(string filePath, OrganizeMode mode)
    {
        var info = new FileInfo(filePath);
        var date = info.CreationTime;

        return mode switch
        {
            OrganizeMode.ByCategory          => string.Empty, // relDest already has category
            OrganizeMode.ByCategoryYearMonth => Path.Combine(date.Year.ToString("D4"), date.Month.ToString("D2")),
            OrganizeMode.ByCreationDate      => date.ToString("yyyy-MM-dd"),
            OrganizeMode.ByModificationDate  => info.LastWriteTime.ToString("yyyy-MM-dd"),
            OrganizeMode.ByExtension         => Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            OrganizeMode.ByFirstLetter       => (Path.GetFileName(filePath).FirstOrDefault() is char c
                                                    ? char.ToUpperInvariant(c).ToString() : "#"),
            OrganizeMode.BySize              => info.Length switch
            {
                < 1_048_576        => "Piccoli",
                < 104_857_600      => "Medi",
                _                  => "Grandi"
            },
            _ => string.Empty
        };
    }

    private static string MakeUniqueDestination(string path)
    {
        if (!File.Exists(path)) return path;
        var dir  = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext  = Path.GetExtension(path);
        int i    = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{stem} ({i++}){ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }

    // ---------------------------------------------------------------------------
    // File enumeration
    // ---------------------------------------------------------------------------

    private List<string> EnumerateFiles(Profile profile)
    {
        var result = new List<string>();
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible     = true,
            RecurseSubdirectories  = profile.IncludeSubfolders,
            AttributesToSkip       = FileAttributes.ReparsePoint
        };

        foreach (var src in profile.SourceFolders)
        {
            if (!Directory.Exists(src)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(src, "*.*", opts))
                {
                    if (!ApplyFilter(file, profile.Filter)) continue;
                    result.Add(file);
                }
            }
            catch { /* inaccessible source: skip */ }
        }
        return result;
    }

    private bool ApplyFilter(string file, ProfileFilter filter)
    {
        // Safe-file level skipping (temp downloads, hidden, system, OneDrive)
        if (_safe.ShouldSkipFile(file)) return false;

        var fi  = new FileInfo(file);
        var ext = Path.GetExtension(file);

        if (filter.ExcludedExtensions.Any(e =>
                string.Equals(e.StartsWith('.') ? e : "." + e, ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (filter.ExcludedFolderPatterns.Any(p =>
                file.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (fi.Length < filter.MinSizeBytes) return false;
        if (fi.Length > filter.MaxSizeBytes) return false;

        if (filter.SkipNewerThanMinutes > 0 &&
            (DateTime.Now - fi.LastWriteTime).TotalMinutes < filter.SkipNewerThanMinutes)
            return false;

        return true;
    }

    private static bool ShouldSkip(string file, Profile profile, out string reason)
    {
        reason = string.Empty;
        if (!File.Exists(file)) { reason = "File non trovato"; return true; }
        return false;
    }

    private static bool ShouldCompress(string file, Profile profile)
        => profile.CompressOldFiles &&
           (DateTime.Now - File.GetLastWriteTime(file)).TotalDays > profile.CompressOlderThanDays;
}
