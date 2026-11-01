using System;
using System.IO;
using System.Linq;
using CosmOrganizer.Core;
using Xunit;

namespace CosmOrganizer.Tests;

public class DiskAndCleanTests : IDisposable
{
    private readonly string _testDir;
    private readonly SafeFileService _safe;
    private readonly JournalService _journal;

    public DiskAndCleanTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CosmoDiskTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _safe = new SafeFileService();
        _journal = new JournalService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public void DriveHelper_GetSystemDrives_ReturnsReadyDrives()
    {
        var drives = DriveHelper.GetSystemDrives();
        Assert.NotNull(drives);
        Assert.NotEmpty(drives);

        var first = drives.First();
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.True(first.TotalBytes > 0);
        Assert.True(first.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(first.FormattedTotal));
        Assert.False(string.IsNullOrWhiteSpace(first.FormattedFree));
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1099511627776, "1.00 TB")]
    public void DriveHelper_FormatBytes_FormatsProperly(long bytes, string expected)
    {
        var res = DriveHelper.FormatBytes(bytes);
        Assert.Equal(expected, res);
    }

    [Fact]
    public void MaintenanceService_ScanForJunk_ScansFolderCorrectly()
    {
        var zero = Path.Combine(_testDir, "test_empty.dat");
        var tmp = Path.Combine(_testDir, "test.tmp");
        var normal = Path.Combine(_testDir, "normal.txt");

        File.WriteAllBytes(zero, Array.Empty<byte>());
        File.WriteAllText(tmp, "Temporary junk data");
        File.WriteAllText(normal, "Keep this file");

        var service = new MaintenanceService(_safe, _journal);
        var junk = service.ScanForJunk(_testDir);

        Assert.Equal(2, junk.Count);
        Assert.Contains(junk, c => c.Path == zero);
        Assert.Contains(junk, c => c.Path == tmp);
        Assert.DoesNotContain(junk, c => c.Path == normal);
    }

    [Fact]
    public void MaintenanceService_RecycleJunkWithDetails_RemovesFilesAndReportsBytes()
    {
        var tmp = Path.Combine(_testDir, "junk_to_delete.tmp");
        File.WriteAllText(tmp, "1234567890"); // 10 bytes

        var service = new MaintenanceService(_safe, _journal);
        var junk = service.ScanForJunk(_testDir);

        var res = service.RecycleJunkWithDetails(junk, "test_run");
        Assert.True(res.Count >= 1);
        Assert.True(res.BytesFreed >= 10);
        Assert.False(File.Exists(tmp));
    }
}
