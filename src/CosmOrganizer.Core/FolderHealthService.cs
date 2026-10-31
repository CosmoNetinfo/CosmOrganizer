// CosmOrganizer.Core - FolderHealthService
// Quick scan of folder status: size by category, oldest & heaviest files,
// empty directories, old installers, and suggested cleanup plan.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

public class FolderCategoryStats
{
    public string CategoryName { get; init; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
}

public class FileSummary
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public DateTime Date { get; init; }
}

public class FolderHealthReport
{
    public string TargetFolder { get; init; } = string.Empty;
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public List<FolderCategoryStats> CategoryBreakdown { get; set; } = new();
    public List<FileSummary> HeaviestFiles { get; set; } = new();
    public List<FileSummary> OldestFiles { get; set; } = new();
    public int EmptyDirectoriesCount { get; set; }
    public int OldInstallersCount { get; set; }
    public int PotentialDuplicatesCount { get; set; }
    public List<string> SuggestedActions { get; set; } = new();
}

public class FolderHealthService
{
    private readonly CategoryService _categories;
    private readonly SafeFileService _safe;

    private static readonly HashSet<string> InstallerExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx"
    };

    public FolderHealthService(CategoryService categories, SafeFileService safe)
    {
        _categories = categories;
        _safe = safe;
    }

    public FolderHealthReport Analyze(string folderPath, bool includeSubfolders = false)
    {
        var report = new FolderHealthReport { TargetFolder = folderPath };
        if (!Directory.Exists(folderPath)) return report;

        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = includeSubfolders,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var allFiles = new List<FileInfo>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*.*", opts))
            {
                if (_safe.ShouldSkipFile(path)) continue;
                try { allFiles.Add(new FileInfo(path)); } catch { }
            }
        }
        catch { }

        report.TotalFiles = allFiles.Count;
        report.TotalBytes = allFiles.Sum(f => f.Length);

        // Category breakdown
        var catMap = new Dictionary<string, FolderCategoryStats>(StringComparer.OrdinalIgnoreCase);
        var sizeMap = new Dictionary<long, int>();

        var now = DateTime.Now;
        int oldInstallers = 0;

        foreach (var fi in allFiles)
        {
            var cat = _categories.GetCategory(fi.FullName);
            if (!catMap.TryGetValue(cat, out var stat))
            {
                stat = new FolderCategoryStats { CategoryName = cat };
                catMap[cat] = stat;
            }
            stat.FileCount++;
            stat.TotalBytes += fi.Length;

            // Old installers check (> 30 days)
            if (InstallerExts.Contains(fi.Extension) && (now - fi.LastWriteTime).TotalDays > 30)
            {
                oldInstallers++;
            }

            // Potential duplicate tracking
            if (fi.Length > 0)
            {
                sizeMap[fi.Length] = sizeMap.GetValueOrDefault(fi.Length, 0) + 1;
            }
        }

        report.CategoryBreakdown = catMap.Values.OrderByDescending(c => c.TotalBytes).ToList();
        report.OldInstallersCount = oldInstallers;
        report.PotentialDuplicatesCount = sizeMap.Values.Where(count => count > 1).Sum(count => count - 1);

        // Heaviest files (top 5)
        report.HeaviestFiles = allFiles
            .OrderByDescending(f => f.Length)
            .Take(5)
            .Select(f => new FileSummary { Path = f.FullName, Name = f.Name, Bytes = f.Length, Date = f.LastWriteTime })
            .ToList();

        // Oldest files (top 5)
        report.OldestFiles = allFiles
            .OrderBy(f => f.LastWriteTime)
            .Take(5)
            .Select(f => new FileSummary { Path = f.FullName, Name = f.Name, Bytes = f.Length, Date = f.LastWriteTime })
            .ToList();

        // Empty directories
        try
        {
            int emptyDirs = 0;
            foreach (var dir in Directory.EnumerateDirectories(folderPath, "*", opts))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        emptyDirs++;
                }
                catch { }
            }
            report.EmptyDirectoriesCount = emptyDirs;
        }
        catch { }

        // Actionable suggestions
        if (report.PotentialDuplicatesCount > 0)
            report.SuggestedActions.Add($"Trovati ~{report.PotentialDuplicatesCount} possibili file duplicati.");
        if (report.OldInstallersCount > 0)
            report.SuggestedActions.Add($"Rilevati {report.OldInstallersCount} installer vecchi scaricati più di 30 giorni fa.");
        if (report.EmptyDirectoriesCount > 0)
            report.SuggestedActions.Add($"Presenti {report.EmptyDirectoriesCount} cartelle vuote da rimuovere.");

        return report;
    }
}
