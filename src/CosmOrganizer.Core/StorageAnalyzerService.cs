// CosmOrganizer.Core - StorageAnalyzerService
// Analyzes large files, heavy directories, and files inactive > N days.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

public class StorageItem
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public DateTime LastModified { get; init; }
    public string FormattedSize => Bytes switch
    {
        >= 1_073_741_824 => $"{Bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{Bytes / 1_048_576.0:F1} MB",
        _ => $"{Bytes / 1024.0:F0} KB"
    };
}

public class DirectoryStorageSummary
{
    public string DirectoryPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    public double PercentageOfTotal { get; set; }
}

public class StorageAnalysisResult
{
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public List<StorageItem> TopLargestFiles { get; set; } = new();
    public List<DirectoryStorageSummary> HeaviestSubdirectories { get; set; } = new();
    public List<StorageItem> InactiveFiles { get; set; } = new();
}

public class StorageAnalyzerService
{
    private readonly SafeFileService _safe;

    public StorageAnalyzerService(SafeFileService safe)
    {
        _safe = safe;
    }

    public StorageAnalysisResult Analyze(
        string rootFolder,
        int inactiveDaysThreshold = 90,
        int topFileCount = 100)
    {
        var result = new StorageAnalysisResult();
        if (!Directory.Exists(rootFolder)) return result;

        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var allItems = new List<StorageItem>();
        var dirSizeMap = new Dictionary<string, (long bytes, int count)>(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var thresholdDate = now.AddDays(-inactiveDaysThreshold);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(rootFolder, "*.*", opts))
            {
                if (_safe.ShouldSkipFile(filePath)) continue;
                try
                {
                    var fi = new FileInfo(filePath);
                    var item = new StorageItem
                    {
                        Path = fi.FullName,
                        Name = fi.Name,
                        Bytes = fi.Length,
                        LastModified = fi.LastWriteTime
                    };
                    allItems.Add(item);

                    // Track direct immediate subdirectory under rootFolder
                    var rel = Path.GetRelativePath(rootFolder, fi.FullName);
                    var firstSep = rel.IndexOf(Path.DirectorySeparatorChar);
                    var subName = firstSep > 0 ? rel[..firstSep] : "Radice";

                    if (!dirSizeMap.TryGetValue(subName, out var tuple))
                        tuple = (0L, 0);

                    dirSizeMap[subName] = (tuple.bytes + fi.Length, tuple.count + 1);
                }
                catch { }
            }
        }
        catch { }

        result.TotalFiles = allItems.Count;
        result.TotalBytes = allItems.Sum(i => i.Bytes);

        // Top N largest files
        result.TopLargestFiles = allItems
            .OrderByDescending(i => i.Bytes)
            .Take(topFileCount)
            .ToList();

        // Inactive files
        result.InactiveFiles = allItems
            .Where(i => i.LastModified < thresholdDate)
            .OrderByDescending(i => i.Bytes)
            .ToList();

        // Heaviest immediate subdirectories
        if (result.TotalBytes > 0)
        {
            result.HeaviestSubdirectories = dirSizeMap
                .Select(kvp => new DirectoryStorageSummary
                {
                    Name = kvp.Key,
                    DirectoryPath = kvp.Key == "Radice" ? rootFolder : Path.Combine(rootFolder, kvp.Key),
                    TotalBytes = kvp.Value.bytes,
                    FileCount = kvp.Value.count,
                    PercentageOfTotal = Math.Round((double)kvp.Value.bytes / result.TotalBytes * 100, 1)
                })
                .OrderByDescending(d => d.TotalBytes)
                .ToList();
        }

        return result;
    }
}
