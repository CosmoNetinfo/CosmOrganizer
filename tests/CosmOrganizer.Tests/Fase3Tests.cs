// CosmOrganizer.Tests - Fase 3 Tests
// Comprehensive tests for all Level A and Level B order-keeping features.
// © CosmoNet.info — MIT License

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CosmOrganizer.Core;
using Xunit;

namespace CosmOrganizer.Tests;

public class Fase3Tests : IDisposable
{
    private readonly SafeFileService _safe = new();
    private readonly CategoryService _categories = new();
    private readonly JournalService _journal = new();
    private readonly List<string> _tempDirs = new();

    public Fase3Tests()
    {
        _categories.Load(string.Empty);
    }

    [Fact]
    public void FolderHealthService_AnalyzesCategoryAndJunkBreakdown()
    {
        var dir = MakeTempDir();
        File.WriteAllText(Path.Combine(dir, "doc.pdf"), "pdf content");
        File.WriteAllText(Path.Combine(dir, "pic.png"), "png content");
        File.WriteAllText(Path.Combine(dir, "empty.txt"), "");
        Directory.CreateDirectory(Path.Combine(dir, "EmptyFolder"));

        var healthSvc = new FolderHealthService(_categories, _safe);
        var report = healthSvc.Analyze(dir, includeSubfolders: true);

        Assert.Equal(3, report.TotalFiles);
        Assert.Equal(1, report.EmptyDirectoriesCount);
        Assert.Contains(report.CategoryBreakdown, c => c.CategoryName == "Documenti");
        Assert.Contains(report.CategoryBreakdown, c => c.CategoryName == "Immagini");
    }

    [Fact]
    public void DuplicateFinderService_FindsDuplicates_AndNeverDeletesAllCopies()
    {
        var dir = MakeTempDir();
        var f1 = Path.Combine(dir, "file1.txt");
        var f2 = Path.Combine(dir, "file1_copy.txt");
        var f3 = Path.Combine(dir, "unique.txt");

        var duplicateContent = "exact same duplicate content in both files for testing";
        File.WriteAllText(f1, duplicateContent);
        File.WriteAllText(f2, duplicateContent);
        File.WriteAllText(f3, "completely different content");

        var dupSvc = new DuplicateFinderService(_safe, _journal);
        var groups = dupSvc.FindDuplicates(new[] { dir }, minSizeBytes: 10);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);

        // Apply KeepOldest strategy
        dupSvc.ApplyAutoSelection(groups, KeepStrategy.KeepOldest);
        Assert.Single(groups[0].Files.Where(f => !f.SelectedForRecycle)); // exactly 1 kept
        Assert.Single(groups[0].Files.Where(f => f.SelectedForRecycle));   // exactly 1 selected

        // Force all selected to test safety guard
        groups[0].Files.ForEach(f => f.SelectedForRecycle = true);
        var recycled = dupSvc.RecycleSelected(groups, "run_dup_test");

        // The safety guard prevents deleting all copies! Exactly 1 must be recycled, 1 remains
        Assert.Equal(1, recycled);
        Assert.True(File.Exists(groups[0].Files[0].Path) || File.Exists(groups[0].Files[1].Path));
    }

    [Fact]
    public void StorageAnalyzerService_ReportsTopLargestAndSubdirectories()
    {
        var root = MakeTempDir();
        var subA = Path.Combine(root, "SubA");
        var subB = Path.Combine(root, "SubB");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);

        var bigFile = Path.Combine(subA, "large.bin");
        File.WriteAllBytes(bigFile, new byte[100 * 1024]); // 100 KB
        var smallFile = Path.Combine(subB, "small.bin");
        File.WriteAllBytes(smallFile, new byte[10 * 1024]); // 10 KB

        var analyzer = new StorageAnalyzerService(_safe);
        var res = analyzer.Analyze(root);

        Assert.Equal(2, res.TotalFiles);
        Assert.Equal(110 * 1024, res.TotalBytes);
        Assert.Equal("large.bin", res.TopLargestFiles.First().Name);
        Assert.Equal("SubA", res.HeaviestSubdirectories.First().Name);
    }

    [Fact]
    public void ArchiveService_MoveByYear_MovesFilesToYearFolder()
    {
        var src = MakeTempDir();
        var dst = MakeTempDir();
        var f1 = Path.Combine(src, "old_doc.txt");
        File.WriteAllText(f1, "legacy text");

        // Set LastWriteTime to 2020
        File.SetLastWriteTime(f1, new DateTime(2020, 5, 1));

        var archiveSvc = new ArchiveService(_safe, _journal);
        var count = archiveSvc.ArchiveOldFiles(src, dst, olderThanDays: 365, ArchiveMode.MoveByYear, "run_arch_test");

        Assert.Equal(1, count);
        Assert.True(File.Exists(Path.Combine(dst, "2020", "old_doc.txt")));
        Assert.False(File.Exists(f1));
    }

    [Fact]
    public void InstallerCleanerService_NormalizesNames_AndKeepsNewestVersion()
    {
        var dir = MakeTempDir();
        var v1 = Path.Combine(dir, "DiscordSetup-1.0.9000.exe");
        var v2 = Path.Combine(dir, "DiscordSetup-1.0.9020.exe");
        File.WriteAllText(v1, "v1 installer");
        File.WriteAllText(v2, "v2 installer");

        File.SetLastWriteTime(v1, DateTime.Now.AddDays(-20));
        File.SetLastWriteTime(v2, DateTime.Now.AddDays(-2));

        var cleaner = new InstallerCleanerService(_safe, _journal);
        var items = cleaner.Analyze(dir, olderThanDays: 30);

        Assert.Equal(2, items.Count);
        Assert.Equal("discord", items[0].NormalizedProgramName);
        Assert.Equal("discord", items[1].NormalizedProgramName);

        // v1 is older version -> marked for recycle
        var oldVer = items.First(i => i.Path == v1);
        var newVer = items.First(i => i.Path == v2);
        Assert.True(oldVer.IsOlderVersion);
        Assert.True(oldVer.SelectedForRecycle);
        Assert.False(newVer.IsOlderVersion);
    }

    [Fact]
    public void MediaSorterService_RoutesScreenshotsAndPhotos()
    {
        var src = MakeTempDir();
        var dst = MakeTempDir();

        var shot = Path.Combine(src, "Screenshot_20260101.png");
        var photo = Path.Combine(src, "IMG_20260202.jpg");
        File.WriteAllText(shot, "screenshot");
        File.WriteAllText(photo, "photo");

        var mediaSvc = new MediaSorterService(_safe, _journal);
        var plan = mediaSvc.Plan(src, dst);

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, p => p.IsScreenshot && p.DestinationPath.Contains("Screenshot"));
        Assert.Contains(plan, p => !p.IsScreenshot && p.DestinationPath.Contains("Foto"));

        var count = mediaSvc.Execute(plan, "run_media_test");
        Assert.Equal(2, count);
    }

    [Fact]
    public void BatchRenamerService_CleansDuplicateMarkersAndAppliesCounter()
    {
        var dir = MakeTempDir();
        var f1 = Path.Combine(dir, "Documento (1).pdf");
        var f2 = Path.Combine(dir, "Copia di Report.pdf");
        File.WriteAllText(f1, "doc 1");
        File.WriteAllText(f2, "report");

        var renamer = new BatchRenamerService(_safe, _journal);
        var opts = new BatchRenameOptions
        {
            CleanDuplicateMarkers = true,
            PatternTemplate = "{nome}_{contatore}",
            CounterStart = 1,
            CounterPadding = 2
        };

        var preview = renamer.Preview(new[] { f1, f2 }, opts);
        Assert.Equal(2, preview.Count);
        Assert.Equal("Documento_01.pdf", preview[0].NewFileName);
        Assert.Equal("Report_02.pdf", preview[1].NewFileName);
    }

    [Fact]
    public void JournalSearchService_FindsFileAcrossRuns()
    {
        var runId = "run_search_" + Guid.NewGuid().ToString("N")[..8];
        var op = new FileOperation(runId, DateTime.UtcNow, "moved", @"C:\Old\ImportantContract.pdf", @"C:\New\ImportantContract.pdf");
        _journal.AppendEntry(op);

        var searchSvc = new JournalSearchService(_journal);
        var results = searchSvc.FindFile("Contract");

        Assert.NotEmpty(results);
        Assert.Equal("ImportantContract.pdf", results[0].FileName);
        Assert.Equal(runId, results[0].RunId);
    }

    [Fact]
    public void MaintenanceService_IdentifiesZeroByteAndTempFiles()
    {
        var dir = MakeTempDir();
        var zero = Path.Combine(dir, "empty.dat");
        var tmp = Path.Combine(dir, "scratch.tmp");
        var lockFile = Path.Combine(dir, "~$WordLock.docx");
        var valid = Path.Combine(dir, "valid.txt");

        File.WriteAllBytes(zero, Array.Empty<byte>());
        File.WriteAllText(tmp, "temp data");
        File.WriteAllText(lockFile, "lock");
        File.WriteAllText(valid, "real user document");

        var maint = new MaintenanceService(_safe, _journal);
        var candidates = maint.ScanForJunk(dir);

        Assert.Equal(3, candidates.Count);
        Assert.Contains(candidates, c => c.Path == zero);
        Assert.Contains(candidates, c => c.Path == tmp);
        Assert.Contains(candidates, c => c.Path == lockFile);
        Assert.DoesNotContain(candidates, c => c.Path == valid);
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CosmoTest_Fase3_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
