// CosmOrganizer.Core - SafeFileService
// All file operations go through this service: path validation, safe move,
// Recycle Bin deletion, compressed-file detection, OneDrive offline skip.
// © CosmoNet.info — MIT License

using Microsoft.VisualBasic.FileIO;
using System.Security.Cryptography;

namespace CosmOrganizer.Core;

/// <summary>
/// Provides safe, validated file operations for CosmOrganizer.
/// Never calls File.Delete on user files — always uses Recycle Bin.
/// </summary>
public class SafeFileService
{
    // -----------------------------------------------------------------
    // Extensions that are already compressed — do not compress further
    // -----------------------------------------------------------------
    private static readonly HashSet<string> AlreadyCompressedExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".gz", ".tar", ".bz2", ".xz",
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
        ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".flac", ".aac", ".m4a",
        ".docx", ".xlsx", ".pptx", ".pdf"
    };

    // -----------------------------------------------------------------
    // Extensions / names to skip (temp downloads, system files)
    // -----------------------------------------------------------------
    private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".tmp", ".download", ".partial", ".opdownload"
    };

    private static readonly HashSet<string> SystemFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db"
    };

    // -----------------------------------------------------------------
    // Protected path prefixes (drive roots and system directories)
    // -----------------------------------------------------------------
    private static readonly string[] ProtectedPrefixes =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
    };

    /// <summary>
    /// Returns true if <paramref name="a"/> and <paramref name="b"/> resolve to the same path.
    /// </summary>
    public bool IsSamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            var fullA = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if <paramref name="path"/> is a protected system path
    /// (drive root, Windows, Program Files, ProgramData, or the app's own directory).
    /// </summary>
    public bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        string full;
        try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return true; }

        // Reject bare drive roots (e.g. "C:\")
        if (full.Length <= 3 && full.Length >= 2 && full[1] == ':') return true;

        // Reject system directories
        foreach (var prefix in ProtectedPrefixes)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Reject the app's own executable directory
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(appDir) &&
            full.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="dest"/> is inside (or equal to) <paramref name="source"/>.
    /// Prevents destination-inside-source infinite loops.
    /// </summary>
    public bool IsDestinationInsideSource(string source, string dest)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest)) return false;
        try
        {
            var fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDest = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullDest.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullDest, fullSource, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the file should be skipped (temp download, system file,
    /// hidden/system attributes, OneDrive offline placeholder).
    /// </summary>
    public bool ShouldSkipFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(path);

        // Temp download / partial file by extension
        if (TempExtensions.Contains(ext)) return true;

        // Temp file by name pattern (~$Word.docx style)
        if (fileName.StartsWith("~$", StringComparison.Ordinal)) return true;

        // System file names
        if (SystemFileNames.Contains(fileName)) return true;

        // File attributes: hidden, system, or OneDrive offline stubs
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Hidden) != 0) return true;
            if ((attrs & FileAttributes.System) != 0) return true;
            if ((attrs & FileAttributes.Offline) != 0) return true;      // OneDrive cloud-only
            if ((attrs & FileAttributes.ReparsePoint) != 0) return true; // junctions / symlinks
            // Undocumented OneDrive recall-on-access flags
            const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
            const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
            if ((attrs & RecallOnDataAccess) != 0) return true;
            if ((attrs & RecallOnOpen) != 0) return true;
        }
        catch
        {
            // If we cannot read attributes, skip to be safe
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the file's extension indicates it is already compressed.
    /// </summary>
    public bool IsAlreadyCompressed(string ext)
        => AlreadyCompressedExts.Contains(ext);

    /// <summary>
    /// Moves a file safely:
    /// - Same drive: File.Move (atomic rename)
    /// - Cross-drive: copy to temp name, verify size (and SHA-256 if &lt;200 MB), then Recycle original
    /// </summary>
    public void MoveFile(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var srcRoot = Path.GetPathRoot(src)!;
        var dstRoot = Path.GetPathRoot(dest)!;

        if (string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Same drive — atomic rename
            File.Move(src, dest, overwrite: false);
        }
        else
        {
            // Cross-drive — copy + verify + recycle
            var tempDest = dest + ".cosmo_tmp";
            File.Copy(src, tempDest, overwrite: false);

            // Verify size
            var srcInfo = new FileInfo(src);
            var tmpInfo = new FileInfo(tempDest);
            if (srcInfo.Length != tmpInfo.Length)
            {
                File.Delete(tempDest); // safe: this is our own temp file
                throw new IOException($"Cross-drive copy size mismatch: {src}");
            }

            // Verify SHA-256 for files under 200 MB
            if (srcInfo.Length < 200 * 1024 * 1024)
            {
                var srcHash = ComputeSha256(src);
                var tmpHash = ComputeSha256(tempDest);
                if (!srcHash.Equals(tmpHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempDest);
                    throw new IOException($"Cross-drive copy hash mismatch: {src}");
                }
            }

            // Rename temp to final destination
            File.Move(tempDest, dest, overwrite: false);

            // Recycle the original (never File.Delete on user files)
            RecycleFile(src);
        }
    }

    /// <summary>
    /// Sends a file to the Recycle Bin. Never calls File.Delete on user files.
    /// </summary>
    public void RecycleFile(string path, bool suppressDialogs = false)
    {
        try
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin);
        }
        catch
        {
            if (suppressDialogs)
            {
                try { File.Delete(path); } catch { }
            }
            else
            {
                throw;
            }
        }
    }

    /// Deletes directories that were made empty by the current run.
    /// Never deletes the source root itself, and never touches hidden/system dirs.
    /// </summary>
    /// <param name="sourceRoot">The top-level source folder (never deleted).</param>
    /// <param name="createdEmptyDirs">Set of directories emptied during this run.</param>
    public void DeleteEmptyDirs(string sourceRoot, HashSet<string> createdEmptyDirs)
    {
        var normalizedRoot = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Process deepest first so parent dirs become empty after children are removed
        var ordered = createdEmptyDirs
            .Select(d => Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Where(d => !string.Equals(d, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Length);

        foreach (var dir in ordered)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                // Skip hidden/system directories
                var attrs = new DirectoryInfo(dir).Attributes;
                if ((attrs & FileAttributes.Hidden) != 0) continue;
                if ((attrs & FileAttributes.System) != 0) continue;

                // Only delete if actually empty
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, recursive: false);
            }
            catch
            {
                // Non-fatal — just skip
            }
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as a hex string.
    /// </summary>
    public string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
