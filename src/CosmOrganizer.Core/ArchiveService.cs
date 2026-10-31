// CosmOrganizer.Core - ArchiveService
// Archives old files (> N days) to Archivio\AAAA or zip per month.
// Fully recorded in journal for undo capability.
// © CosmoNet.info — MIT License

using System.IO.Compression;

namespace CosmOrganizer.Core;

public enum ArchiveMode
{
    MoveByYear,
    ZipByMonth
}

public class ArchiveEstimate
{
    public int FileCount { get; set; }
    public long OriginalBytes { get; set; }
    public long EstimatedSavedBytes { get; set; }
}

public class ArchiveService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    public ArchiveService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    public ArchiveEstimate Estimate(string sourceFolder, int olderThanDays)
    {
        var estimate = new ArchiveEstimate();
        if (!Directory.Exists(sourceFolder)) return estimate;

        var threshold = DateTime.Now.AddDays(-olderThanDays);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*", opts))
        {
            if (_safe.ShouldSkipFile(file)) continue;
            try
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < threshold)
                {
                    estimate.FileCount++;
                    estimate.OriginalBytes += fi.Length;
                    if (!_safe.IsAlreadyCompressed(fi.Extension))
                    {
                        // Assume ~40% space savings for compressible formats
                        estimate.EstimatedSavedBytes += (long)(fi.Length * 0.4);
                    }
                }
            }
            catch { }
        }

        return estimate;
    }

    public int ArchiveOldFiles(
        string sourceFolder,
        string targetArchiveBase,
        int olderThanDays,
        ArchiveMode mode,
        string runId)
    {
        if (!Directory.Exists(sourceFolder)) return 0;
        Directory.CreateDirectory(targetArchiveBase);

        var threshold = DateTime.Now.AddDays(-olderThanDays);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        int processed = 0;

        if (mode == ArchiveMode.MoveByYear)
        {
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*", opts))
            {
                if (_safe.ShouldSkipFile(file)) continue;
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTime < threshold)
                    {
                        var yearDir = Path.Combine(targetArchiveBase, fi.LastWriteTime.Year.ToString("D4"));
                        Directory.CreateDirectory(yearDir);

                        var dest = Path.Combine(yearDir, fi.Name);
                        _safe.MoveFile(file, dest);
                        _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "moved", file, dest));
                        processed++;
                    }
                }
                catch { }
            }
        }
        else if (mode == ArchiveMode.ZipByMonth)
        {
            // Group files by Year-Month
            var monthGroups = new Dictionary<string, List<string>>();
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*", opts))
            {
                if (_safe.ShouldSkipFile(file)) continue;
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTime < threshold)
                    {
                        var key = fi.LastWriteTime.ToString("yyyy-MM");
                        if (!monthGroups.TryGetValue(key, out var list))
                        {
                            list = new List<string>();
                            monthGroups[key] = list;
                        }
                        list.Add(file);
                    }
                }
                catch { }
            }

            foreach (var (month, files) in monthGroups)
            {
                var zipPath = Path.Combine(targetArchiveBase, $"{month}.zip");
                using (var zip = ZipFile.Open(zipPath, File.Exists(zipPath) ? ZipArchiveMode.Update : ZipArchiveMode.Create))
                {
                    foreach (var f in files)
                    {
                        try
                        {
                            var entryName = Path.GetFileName(f);
                            zip.CreateEntryFromFile(f, entryName);
                            _safe.RecycleFile(f);
                            _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "compressed", f, zipPath));
                            processed++;
                        }
                        catch { }
                    }
                }
            }
        }

        return processed;
    }
}
