// CosmOrganizer.Core - InstallerCleanerService
// Finds old installers and detects multi-version duplicates (keeping only newest).
// © CosmoNet.info — MIT License

using System.Text.RegularExpressions;

namespace CosmOrganizer.Core;

public class InstallerItem
{
    public string Path { get; init; } = string.Empty;
    public string NormalizedProgramName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public DateTime LastModified { get; init; }
    public bool IsOlderVersion { get; set; }
    public bool IsOlderThanDays { get; set; }
    public bool SelectedForRecycle { get; set; }
}

public class InstallerCleanerService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    private static readonly HashSet<string> InstallerExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx"
    };

    // Regex to strip version patterns like "-1.2.3", "_v2.0", "Setup", "Installer", " x64", "(1)"
    private static readonly Regex VersionPattern = new(
        @"[-_.]?(v?\d+(\.\d+)+|\bx64\b|\bx86\b|setup|installer|\(\d+\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public InstallerCleanerService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    public List<InstallerItem> Analyze(string folder, int olderThanDays = 30)
    {
        var results = new List<InstallerItem>();
        if (!Directory.Exists(folder)) return results;

        var threshold = DateTime.Now.AddDays(-olderThanDays);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };

        var items = new List<InstallerItem>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", opts))
        {
            if (_safe.ShouldSkipFile(file)) continue;
            var ext = Path.GetExtension(file);
            if (!InstallerExts.Contains(ext)) continue;

            try
            {
                var fi = new FileInfo(file);
                var normName = NormalizeInstallerName(fi.Name);

                items.Add(new InstallerItem
                {
                    Path = file,
                    FileName = fi.Name,
                    NormalizedProgramName = normName,
                    Bytes = fi.Length,
                    LastModified = fi.LastWriteTime,
                    IsOlderThanDays = fi.LastWriteTime < threshold
                });
            }
            catch { }
        }

        // Group by normalized program name
        var grouped = items.GroupBy(i => i.NormalizedProgramName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var ordered = group.OrderByDescending(i => i.LastModified).ToList();
            if (ordered.Count > 1)
            {
                // Newest is at index 0; all older versions marked
                for (int i = 1; i < ordered.Count; i++)
                {
                    ordered[i].IsOlderVersion = true;
                    ordered[i].SelectedForRecycle = true;
                }
            }

            // Also select if older than N days
            foreach (var item in ordered)
            {
                if (item.IsOlderThanDays) item.SelectedForRecycle = true;
            }

            results.AddRange(ordered);
        }

        return results.OrderByDescending(i => i.LastModified).ToList();
    }

    public int RecycleSelected(IEnumerable<InstallerItem> items, string runId)
    {
        int count = 0;
        foreach (var item in items.Where(i => i.SelectedForRecycle))
        {
            try
            {
                _safe.RecycleFile(item.Path);
                _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "recycled", item.Path, string.Empty));
                count++;
            }
            catch { }
        }
        return count;
    }

    public static string NormalizeInstallerName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var clean = VersionPattern.Replace(stem, string.Empty).Trim(' ', '-', '_');
        return string.IsNullOrWhiteSpace(clean) ? stem : clean.ToLowerInvariant();
    }
}
