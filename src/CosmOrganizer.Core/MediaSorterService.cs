// CosmOrganizer.Core - MediaSorterService
// Organizes screenshots and photos by camera capture date (EXIF DateTimeOriginal).
// © CosmoNet.info — MIT License

using System.Text;

namespace CosmOrganizer.Core;

public class MediaSortResult
{
    public string OriginalPath { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public bool IsScreenshot { get; init; }
    public DateTime DateTaken { get; init; }
}

public class MediaSorterService
{
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    private static readonly string[] ScreenshotPrefixes =
    {
        "screenshot", "schermata", "cattura", "snip", "captura"
    };

    private static readonly string[] CameraPrefixes =
    {
        "img_", "pxl_", "whatsapp image", "dscn", "sam_"
    };

    private static readonly HashSet<string> PhotoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".webp", ".raw", ".dng"
    };

    public MediaSorterService(SafeFileService safe, JournalService journal)
    {
        _safe = safe;
        _journal = journal;
    }

    public List<MediaSortResult> Plan(string sourceFolder, string targetBaseFolder)
    {
        var plan = new List<MediaSortResult>();
        if (!Directory.Exists(sourceFolder)) return plan;

        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*", opts))
        {
            if (_safe.ShouldSkipFile(file)) continue;
            var ext = Path.GetExtension(file);
            if (!PhotoExts.Contains(ext)) continue;

            var fileName = Path.GetFileName(file);
            var lowerName = fileName.ToLowerInvariant();

            bool isScreenshot = ScreenshotPrefixes.Any(p => lowerName.StartsWith(p));
            bool isCamera = CameraPrefixes.Any(p => lowerName.StartsWith(p));

            string dest;
            var date = ExtractDateTaken(file);

            if (isScreenshot)
            {
                dest = Path.Combine(targetBaseFolder, "Immagini", "Screenshot", fileName);
            }
            else if (isCamera || PhotoExts.Contains(ext))
            {
                // Organize photos by Year\Month of capture
                dest = Path.Combine(
                    targetBaseFolder, "Foto",
                    date.Year.ToString("D4"),
                    date.Month.ToString("D2"),
                    fileName);
            }
            else
            {
                continue;
            }

            plan.Add(new MediaSortResult
            {
                OriginalPath = file,
                DestinationPath = dest,
                IsScreenshot = isScreenshot,
                DateTaken = date
            });
        }

        return plan;
    }

    public int Execute(List<MediaSortResult> plan, string runId)
    {
        int count = 0;
        foreach (var item in plan)
        {
            if (_safe.IsSamePath(item.OriginalPath, item.DestinationPath)) continue;
            try
            {
                _safe.MoveFile(item.OriginalPath, item.DestinationPath);
                _journal.AppendEntry(new FileOperation(runId, DateTime.UtcNow, "moved", item.OriginalPath, item.DestinationPath));
                count++;
            }
            catch { }
        }
        return count;
    }

    /// <summary>
    /// Reads EXIF DateTimeOriginal from JPEG APP1 marker; falls back to file LastWriteTime.
    /// </summary>
    public static DateTime ExtractDateTaken(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg")
            {
                var exifDate = TryReadExifDateTimeOriginal(filePath);
                if (exifDate.HasValue) return exifDate.Value;
            }
            return File.GetLastWriteTime(filePath);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static DateTime? TryReadExifDateTimeOriginal(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var reader = new BinaryReader(fs);

            // Verify SOI marker (0xFF, 0xD8)
            if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8) return null;

            while (fs.Position < fs.Length - 4)
            {
                byte markerLead = reader.ReadByte();
                if (markerLead != 0xFF) continue;

                byte marker = reader.ReadByte();
                // SOS marker starts scan data -> stop
                if (marker == 0xDA) break;

                // Big endian segment length
                ushort len = (ushort)((reader.ReadByte() << 8) | reader.ReadByte());
                if (len < 2) break;

                if (marker == 0xE1) // APP1 Exif marker
                {
                    var data = reader.ReadBytes(len - 2);
                    // Search for "YYYY:MM:DD HH:MM:SS" ASCII pattern in APP1
                    var str = Encoding.ASCII.GetString(data);
                    for (int i = 0; i <= str.Length - 19; i++)
                    {
                        if (char.IsDigit(str[i]) && char.IsDigit(str[i + 1]) && char.IsDigit(str[i + 2]) && char.IsDigit(str[i + 3])
                            && str[i + 4] == ':' && char.IsDigit(str[i + 5]) && char.IsDigit(str[i + 6])
                            && str[i + 7] == ':' && char.IsDigit(str[i + 8]) && char.IsDigit(str[i + 9])
                            && str[i + 10] == ' ' && char.IsDigit(str[i + 11]) && char.IsDigit(str[i + 12])
                            && str[i + 13] == ':' && char.IsDigit(str[i + 14]) && char.IsDigit(str[i + 15]))
                        {
                            var datePart = str.Substring(i, 19);
                            if (DateTime.TryParseExact(datePart, "yyyy:MM:dd HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var parsed))
                            {
                                return parsed;
                            }
                        }
                    }
                }
                else
                {
                    fs.Seek(len - 2, SeekOrigin.Current);
                }
            }
        }
        catch { }

        return null;
    }
}
