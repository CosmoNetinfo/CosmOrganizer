// CosmOrganizer.Core - BatchRenamerService
// Batch rename files with templates, counter, regex search/replace, and cleanup.
// Includes dry-run preview and journal-tracked execution.
// © CosmoNet.info — MIT License

using System.Text.RegularExpressions;

namespace CosmOrganizer.Core;

public class RenameItem
{
    public string OriginalPath { get; init; } = string.Empty;
    public string NewPath { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string NewFileName { get; init; } = string.Empty;
    public bool HasConflict { get; set; }
}

public class BatchRenameOptions
{
    public string? PatternTemplate { get; set; } // e.g. "Doc_{anno}_{contatore}"
    public string? RegexSearch { get; set; }
    public string? RegexReplace { get; set; }
    public bool CleanDuplicateMarkers { get; set; } = true; // Removes " (1)", "- Copia", "Copia di "
    public int CounterStart { get; set; } = 1;
    public int CounterPadding { get; set; } = 3; // e.g. "001"
}

public class BatchRenamerService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    private static readonly Regex CopyPattern = new(
        @"(^Copia di\s*|\s*-\s*Copia\b|\s*\(\d+\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DoubleSpacePattern = new(
        @"\s{2,}", RegexOptions.Compiled);

    public BatchRenamerService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    public List<RenameItem> Preview(IEnumerable<string> filePaths, BatchRenameOptions opts)
    {
        var items = new List<RenameItem>();
        int counter = opts.CounterStart;
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var dir = Path.GetDirectoryName(path)!;
            var ext = Path.GetExtension(path);
            var stem = Path.GetFileNameWithoutExtension(path);

            string newStem = stem;

            // 1. Clean duplicate markers
            if (opts.CleanDuplicateMarkers)
            {
                newStem = CopyPattern.Replace(newStem, string.Empty);
                newStem = DoubleSpacePattern.Replace(newStem, " ").Trim();
            }

            // 2. Regex search & replace
            if (!string.IsNullOrEmpty(opts.RegexSearch))
            {
                try
                {
                    newStem = Regex.Replace(newStem, opts.RegexSearch, opts.RegexReplace ?? string.Empty, RegexOptions.IgnoreCase);
                }
                catch { }
            }

            // 3. Template pattern
            if (!string.IsNullOrEmpty(opts.PatternTemplate))
            {
                var fi = new FileInfo(path);
                var created = fi.CreationTime;
                var cntStr = counter.ToString().PadLeft(opts.CounterPadding, '0');

                newStem = opts.PatternTemplate
                    .Replace("{nome}", newStem, StringComparison.OrdinalIgnoreCase)
                    .Replace("{ext}", ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                    .Replace("{contatore}", cntStr, StringComparison.OrdinalIgnoreCase)
                    .Replace("{anno}", created.Year.ToString("D4"), StringComparison.OrdinalIgnoreCase)
                    .Replace("{mese}", created.Month.ToString("D2"), StringComparison.OrdinalIgnoreCase)
                    .Replace("{giorno}", created.Day.ToString("D2"), StringComparison.OrdinalIgnoreCase);
            }

            // Clean invalid filename characters
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                newStem = newStem.Replace(invalidChar.ToString(), string.Empty);
            }

            var newFileName = newStem + ext;
            var newFullPath = Path.Combine(dir, newFileName);

            bool conflict = false;
            if (!_safe.IsSamePath(path, newFullPath))
            {
                if (File.Exists(newFullPath) || seenTargets.Contains(newFullPath))
                    conflict = true;
                seenTargets.Add(newFullPath);
            }

            items.Add(new RenameItem
            {
                OriginalPath = path,
                NewPath = newFullPath,
                OriginalFileName = Path.GetFileName(path),
                NewFileName = newFileName,
                HasConflict = conflict
            });

            counter++;
        }

        return items;
    }

    public int Execute(List<RenameItem> items, string runId)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.HasConflict) continue;
            if (_safe.IsSamePath(item.OriginalPath, item.NewPath)) continue;

            try
            {
                File.Move(item.OriginalPath, item.NewPath, overwrite: false);
                _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "moved", item.OriginalPath, item.NewPath));
                count++;
            }
            catch { }
        }
        return count;
    }
}
