// CosmOrganizer.Core - SystemDriveInfo
// Provides drive information, storage stats, and byte formatting.
// © CosmoNet.info — MIT License

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;

namespace CosmOrganizer.Core;

public class SystemDriveInfo
{
    public string Name { get; set; } = string.Empty; // e.g. "C:\"
    public string VolumeLabel { get; set; } = string.Empty; // e.g. "Windows"
    public string DriveType { get; set; } = string.Empty; // "Fixed", "Removable", etc.
    public string DriveFormat { get; set; } = string.Empty; // "NTFS", etc.
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes { get; set; }
    public double UsedPercentage { get; set; }
    public string FormattedTotal { get; set; } = string.Empty;
    public string FormattedFree { get; set; } = string.Empty;
    public string FormattedUsed { get; set; } = string.Empty;
    public bool IsReady { get; set; }
}

public static class DriveHelper
{
    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_099_511_627_776 => (bytes / 1_099_511_627_776.0).ToString("F2", CultureInfo.InvariantCulture) + " TB",
            >= 1_073_741_824 => (bytes / 1_073_741_824.0).ToString("F2", CultureInfo.InvariantCulture) + " GB",
            >= 1_048_576 => (bytes / 1_048_576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB",
            >= 1024 => (bytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture) + " KB",
            _ => $"{bytes} B"
        };
    }

    public static List<SystemDriveInfo> GetSystemDrives()
    {
        var list = new List<SystemDriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = Math.Max(0, total - free);
                    double pct = total > 0 ? Math.Round((double)used / total * 100, 1) : 0;
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? (drive.DriveType == DriveType.Fixed ? "Disco Locale" : "Unità Rimovibile")
                        : drive.VolumeLabel;

                    list.Add(new SystemDriveInfo
                    {
                        Name = drive.Name,
                        VolumeLabel = label,
                        DriveType = drive.DriveType.ToString(),
                        DriveFormat = drive.DriveFormat,
                        TotalBytes = total,
                        FreeBytes = free,
                        UsedBytes = used,
                        UsedPercentage = pct,
                        FormattedTotal = FormatBytes(total),
                        FormattedFree = FormatBytes(free),
                        FormattedUsed = FormatBytes(used),
                        IsReady = true
                    });
                }
            }
            catch { }
        }
        return list;
    }
}
