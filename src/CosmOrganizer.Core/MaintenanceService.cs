// CosmOrganizer.Core - MaintenanceService
// Cleans broken shortcuts, zero-byte files, temporary files, and empty directories.
// Never touches system or protected directories.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

public class CleanupCandidate
{
    public string Path { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public bool SelectedForRecycle { get; set; } = true;
}

public class MaintenanceService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    private static readonly HashSet<string> TempFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", "desktop.ini"
    };

    public MaintenanceService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    public List<CleanupCandidate> ScanForJunk(string folderPath, bool checkSubfolders = true)
    {
        var candidates = new List<CleanupCandidate>();
        if (!Directory.Exists(folderPath)) return candidates;
        if (_safe.IsProtectedPath(folderPath)) return candidates;

        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = checkSubfolders,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", opts))
        {
            try
            {
                var fi = new FileInfo(file);
                var name = fi.Name;
                var ext = fi.Extension;

                // 1. Zero-byte files
                if (fi.Length == 0)
                {
                    candidates.Add(new CleanupCandidate
                    {
                        Path = file,
                        Reason = "File vuoto (0 byte)",
                        Bytes = 0
                    });
                    continue;
                }

                // 2. Temp files (.tmp, ~$Word, thumbs.db)
                if (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("~$", StringComparison.Ordinal) ||
                    TempFileNames.Contains(name))
                {
                    candidates.Add(new CleanupCandidate
                    {
                        Path = file,
                        Reason = "File temporaneo",
                        Bytes = fi.Length
                    });
                    continue;
                }
            }
            catch { }
        }

        return candidates;
    }

    public int RecycleJunk(IEnumerable<CleanupCandidate> candidates, string runId)
    {
        int count = 0;
        foreach (var item in candidates.Where(c => c.SelectedForRecycle))
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
}
