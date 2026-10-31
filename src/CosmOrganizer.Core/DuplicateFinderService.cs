// CosmOrganizer.Core - DuplicateFinderService
// Finds duplicates across folders via size -> partial 64KB hash -> full SHA-256.
// Supports safe automatic selection presets. Never deletes all copies in a group.
// © CosmoNet.info — MIT License

using System.Security.Cryptography;

namespace CosmOrganizer.Core;

public enum KeepStrategy
{
    KeepOldest,
    KeepNewest,
    KeepShortestName,
    KeepInPreferredFolder
}

public class DuplicateFileItem
{
    public string Path { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public DateTime LastModified { get; init; }
    public bool SelectedForRecycle { get; set; }
}

public class DuplicateGroup
{
    public string Sha256Hash { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public List<DuplicateFileItem> Files { get; set; } = new();
}

public class DuplicateFinderService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    public DuplicateFinderService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    /// <summary>
    /// Scans folders and returns groups of duplicate files.
    /// </summary>
    public List<DuplicateGroup> FindDuplicates(
        IEnumerable<string> folders,
        long minSizeBytes = 1024,
        bool includeSubfolders = true)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = includeSubfolders,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        // 1. Group by file size
        var sizeGroups = new Dictionary<long, List<string>>();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.*", opts))
                {
                    if (_safe.ShouldSkipFile(file)) continue;
                    try
                    {
                        var len = new FileInfo(file).Length;
                        if (len >= minSizeBytes)
                        {
                            if (!sizeGroups.TryGetValue(len, out var list))
                            {
                                list = new List<string>();
                                sizeGroups[len] = list;
                            }
                            list.Add(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Keep sizes with 2 or more files
        var potentialCandidates = sizeGroups.Values.Where(g => g.Count > 1).ToList();

        // 2. Group by partial 64KB hash
        var partialGroups = new Dictionary<string, List<string>>();
        foreach (var group in potentialCandidates)
        {
            foreach (var file in group)
            {
                var partialHash = ComputePartialHash(file, 64 * 1024);
                if (string.IsNullOrEmpty(partialHash)) continue;

                var key = $"{new FileInfo(file).Length}_{partialHash}";
                if (!partialGroups.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    partialGroups[key] = list;
                }
                list.Add(file);
            }
        }

        // 3. Group by full SHA-256 for colliding partial hashes
        var fullGroups = new Dictionary<string, List<string>>();
        foreach (var group in partialGroups.Values.Where(g => g.Count > 1))
        {
            foreach (var file in group)
            {
                try
                {
                    var fullHash = _safe.ComputeSha256(file);
                    if (!fullGroups.TryGetValue(fullHash, out var list))
                    {
                        list = new List<string>();
                        fullGroups[fullHash] = list;
                    }
                    list.Add(file);
                }
                catch { }
            }
        }

        // 4. Build DuplicateGroup objects
        var result = new List<DuplicateGroup>();
        foreach (var (hash, files) in fullGroups.Where(kvp => kvp.Value.Count > 1))
        {
            var firstInfo = new FileInfo(files[0]);
            var dupGroup = new DuplicateGroup
            {
                Sha256Hash = hash,
                FileSizeBytes = firstInfo.Length,
                Files = files.Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new DuplicateFileItem
                    {
                        Path = f,
                        Bytes = fi.Length,
                        LastModified = fi.LastWriteTime
                    };
                }).ToList()
            };
            result.Add(dupGroup);
        }

        return result.OrderByDescending(g => g.FileSizeBytes * (g.Files.Count - 1)).ToList();
    }

    /// <summary>
    /// Applies an automated selection strategy across duplicate groups.
    /// In each group, exactly one file is kept; all others are selected for recycling.
    /// </summary>
    public void ApplyAutoSelection(
        List<DuplicateGroup> groups,
        KeepStrategy strategy,
        string? preferredFolder = null)
    {
        foreach (var group in groups)
        {
            if (group.Files.Count <= 1) continue;

            DuplicateFileItem toKeep = strategy switch
            {
                KeepStrategy.KeepOldest => group.Files.OrderBy(f => f.LastModified).First(),
                KeepStrategy.KeepNewest => group.Files.OrderByDescending(f => f.LastModified).First(),
                KeepStrategy.KeepShortestName => group.Files.OrderBy(f => Path.GetFileName(f.Path).Length).First(),
                KeepStrategy.KeepInPreferredFolder when !string.IsNullOrEmpty(preferredFolder) =>
                    group.Files.FirstOrDefault(f => f.Path.StartsWith(preferredFolder, StringComparison.OrdinalIgnoreCase))
                    ?? group.Files.OrderBy(f => f.LastModified).First(),
                _ => group.Files.OrderBy(f => f.LastModified).First()
            };

            foreach (var file in group.Files)
            {
                file.SelectedForRecycle = (file != toKeep);
            }
        }
    }

    /// <summary>
    /// Recycles selected duplicates to the Windows Recycle Bin and records them in the journal.
    /// Enforces the safety rule: NEVER deletes all copies in any group.
    /// </summary>
    public int RecycleSelected(List<DuplicateGroup> groups, string runId)
    {
        int recycledCount = 0;

        foreach (var group in groups)
        {
            // Safety guard: ensure at least one copy is NOT selected
            if (group.Files.All(f => f.SelectedForRecycle))
            {
                // Force unselect the first copy to prevent total data loss!
                group.Files[0].SelectedForRecycle = false;
            }

            foreach (var file in group.Files.Where(f => f.SelectedForRecycle))
            {
                try
                {
                    _safe.RecycleFile(file.Path);
                    _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "recycled", file.Path, string.Empty));
                    recycledCount++;
                }
                catch { }
            }
        }

        return recycledCount;
    }

    private static string ComputePartialHash(string filePath, int bytesToRead)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[bytesToRead];
            int read = stream.Read(buffer, 0, bytesToRead);
            if (read <= 0) return string.Empty;

            var hash = SHA256.HashData(buffer.AsSpan(0, read));
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }
}
