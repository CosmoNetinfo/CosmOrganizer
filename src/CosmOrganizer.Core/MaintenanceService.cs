// CosmOrganizer.Core - MaintenanceService
// Cleans broken shortcuts, zero-byte files, temporary files, and empty directories.
// Never touches system or protected directories.
// © CosmoNet.info — MIT License

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace CosmOrganizer.Core;

public class CleanupCandidate
{
    public string Path { get; init; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Reason { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public string FormattedSize => DriveHelper.FormatBytes(Bytes);
    public bool SelectedForRecycle { get; set; } = true;
}

public class JunkRecycleResult
{
    public int Count { get; set; }
    public long BytesFreed { get; set; }
    public string FormattedBytes => DriveHelper.FormatBytes(BytesFreed);
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

    public List<CleanupCandidate> ScanForJunk(string targetPath, bool checkSubfolders = true)
    {
        if (string.IsNullOrWhiteSpace(targetPath) ||
            targetPath.Equals("SYSTEM_TEMP", StringComparison.OrdinalIgnoreCase) ||
            targetPath.Equals("TEMP", StringComparison.OrdinalIgnoreCase))
        {
            return ScanSystemAndUserTemp();
        }

        string full = targetPath;
        try { full = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { }

        // If it's a bare drive root (e.g. "C:" or "C:\")
        if (full.Length <= 3 && full.Length >= 2 && full[1] == ':')
        {
            return ScanDrive(targetPath);
        }

        var candidates = new List<CleanupCandidate>();
        if (!Directory.Exists(targetPath)) return candidates;
        if (_safe.IsProtectedPath(targetPath)) return candidates;

        ScanDirectory(targetPath, checkSubfolders, candidates);
        return candidates;
    }

    public List<CleanupCandidate> ScanSystemAndUserTemp()
    {
        var candidates = new List<CleanupCandidate>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tempDirs = new List<string>
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var dir in tempDirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.*", opts))
                {
                    if (!scannedPaths.Add(file)) continue;
                    try
                    {
                        var fi = new FileInfo(file);
                        var name = fi.Name;
                        var ext = fi.Extension;

                        if (fi.Length == 0)
                        {
                            candidates.Add(new CleanupCandidate
                            {
                                Path = file,
                                Reason = "File vuoto (0 byte)",
                                Bytes = 0
                            });
                        }
                        else if (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".temp", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".old", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".dmp", StringComparison.OrdinalIgnoreCase) ||
                                 name.StartsWith("~$", StringComparison.Ordinal) ||
                                 TempFileNames.Contains(name) ||
                                 fi.LastWriteTime < DateTime.Now.AddHours(-1))
                        {
                            string reason = ext.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
                                ? "Crash Dump di sistema"
                                : (ext.Equals(".log", StringComparison.OrdinalIgnoreCase) ? "File di log temporaneo" : "File temporaneo");

                            candidates.Add(new CleanupCandidate
                            {
                                Path = file,
                                Reason = reason,
                                Bytes = fi.Length
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return candidates;
    }

    public List<CleanupCandidate> ScanDrive(string drivePath)
    {
        var candidates = new List<CleanupCandidate>();
        var root = Path.GetPathRoot(drivePath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return candidates;

        bool isSystemDrive = string.Equals(root, Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase);

        if (isSystemDrive)
        {
            candidates.AddRange(ScanSystemAndUserTemp());
        }

        try
        {
            var topDirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });

            foreach (var subDir in topDirs)
            {
                var dirName = Path.GetFileName(subDir);
                if (dirName.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (isSystemDrive && (
                    dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                ScanDirectory(subDir, checkSubfolders: true, candidates);
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                CheckAndAddCandidate(file, candidates);
            }
        }
        catch { }

        return candidates;
    }

    private void ScanDirectory(string folderPath, bool checkSubfolders, List<CleanupCandidate> candidates)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = checkSubfolders,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", opts))
            {
                CheckAndAddCandidate(file, candidates);
            }
        }
        catch { }
    }

    private void CheckAndAddCandidate(string file, List<CleanupCandidate> candidates)
    {
        try
        {
            var fi = new FileInfo(file);
            var name = fi.Name;
            var ext = fi.Extension;

            if (fi.Length == 0)
            {
                candidates.Add(new CleanupCandidate
                {
                    Path = file,
                    Reason = "File vuoto (0 byte)",
                    Bytes = 0
                });
                return;
            }

            if (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".temp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".dmp", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("~$", StringComparison.Ordinal) ||
                TempFileNames.Contains(name))
            {
                candidates.Add(new CleanupCandidate
                {
                    Path = file,
                    Reason = "File temporaneo",
                    Bytes = fi.Length
                });
            }
        }
        catch { }
    }

    public JunkRecycleResult RecycleJunkWithDetails(IEnumerable<CleanupCandidate> candidates, string runId)
    {
        int count = 0;
        long bytesFreed = 0;
        foreach (var item in candidates.Where(c => c.SelectedForRecycle))
        {
            try
            {
                if (File.Exists(item.Path))
                {
                    long len = 0;
                    try { len = new FileInfo(item.Path).Length; } catch { }

                    _safe.RecycleFile(item.Path, suppressDialogs: true);
                    _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "recycled", item.Path, string.Empty));
                    count++;
                    bytesFreed += len;
                }
            }
            catch { }
        }
        return new JunkRecycleResult { Count = count, BytesFreed = bytesFreed };
    }

    public int RecycleJunk(IEnumerable<CleanupCandidate> candidates, string runId)
        => RecycleJunkWithDetails(candidates, runId).Count;
}
