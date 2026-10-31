// CosmOrganizer.Tests - Fase 2 Tests
// Tests profiles, rules, categories, preview, scheduler and undo.
// © CosmoNet.info — MIT License

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CosmOrganizer.Core;
using Xunit;

namespace CosmOrganizer.Tests;

public class Fase2Tests : IDisposable
{
    private readonly SafeFileService _safe = new();
    private readonly CategoryService _categories = new();
    private readonly RuleEngine _ruleEngine = new();
    private readonly JournalService _journal = new();
    private readonly List<string> _tempDirs = new();

    public Fase2Tests()
    {
        // Load default categories
        _categories.Load(string.Empty);
    }

    [Fact]
    public void CategoryService_ResolvesKnownAndUnknownExtensions()
    {
        Assert.Equal("Immagini", _categories.GetCategory("photo.jpg"));
        Assert.Equal("Video", _categories.GetCategory("clip.mp4"));
        Assert.Equal("Audio", _categories.GetCategory("song.mp3"));
        Assert.Equal("Documenti", _categories.GetCategory("report.pdf"));
        Assert.Equal("Fogli", _categories.GetCategory("budget.xlsx"));
        Assert.Equal("Presentazioni", _categories.GetCategory("pitch.pptx"));
        Assert.Equal("Archivi", _categories.GetCategory("backup.zip"));
        Assert.Equal("Installer", _categories.GetCategory("setup.exe"));
        Assert.Equal("Sviluppo", _categories.GetCategory("code.cs"));
        Assert.Equal("Font", _categories.GetCategory("typeface.ttf"));
        Assert.Equal("Da smistare", _categories.GetCategory("unknown.xyz123"));
    }

    [Fact]
    public void RuleEngine_MatchesConditionsAndResolvesTemplate()
    {
        var tempDir = MakeTempDir();
        var pdfFile = Path.Combine(tempDir, "fattura_2026_01.pdf");
        File.WriteAllText(pdfFile, "dummy invoice");

        var rule = new CustomRule
        {
            Name = "Fatture PDF",
            Logic = ConditionLogic.And,
            Conditions = new()
            {
                new() { Extensions = new() { ".pdf" } },
                new() { NameMatch = NameMatchType.Contains, NameValue = "fattura" }
            },
            Action = RuleAction.Move,
            DestinationTemplate = "Documenti\\Fatture"
        };

        var match = _ruleEngine.Evaluate(pdfFile, new[] { rule });

        Assert.NotNull(match);
        Assert.Equal(rule, match.Rule);
        Assert.Equal("Documenti\\Fatture", match.ResolvedDestination);
    }

    [Fact]
    public void RuleEngine_TemplatePlaceholders_Resolved()
    {
        var tempDir = MakeTempDir();
        var file = Path.Combine(tempDir, "document.pdf");
        File.WriteAllText(file, "content");

        var resolved = _ruleEngine.ResolveTemplate("{nome}_{ext}_{anno}", file);
        var expectedYear = DateTime.Now.Year.ToString("D4");

        Assert.Equal($"document_pdf_{expectedYear}", resolved);
    }

    [Fact]
    public void Profile_DownloadWithThreeRules_OrganizesCorrectly()
    {
        var srcDir = MakeTempDir();
        var destDir = MakeTempDir();

        // 1. Fattura PDF
        var f1 = Path.Combine(srcDir, "fattura_luce.pdf");
        File.WriteAllText(f1, "fattura pdf");

        // 2. Screenshot
        var f2 = Path.Combine(srcDir, "Screenshot_2026.png");
        File.WriteAllText(f2, "screenshot image");

        // 3. Installer
        var f3 = Path.Combine(srcDir, "setup.exe");
        File.WriteAllText(f3, "installer binary");

        // 4. File senza regola (fallback a categoria)
        var f4 = Path.Combine(srcDir, "canzone.mp3");
        File.WriteAllText(f4, "audio stream");

        var profile = new Profile
        {
            Name = "Download",
            SourceFolders = new() { srcDir },
            DestinationFolder = destDir,
            OrganizeMode = OrganizeMode.ByCategory,
            Rules = new()
            {
                new CustomRule
                {
                    Name = "Fatture PDF",
                    Order = 0,
                    Logic = ConditionLogic.And,
                    Conditions = new()
                    {
                        new() { Extensions = new() { ".pdf" } },
                        new() { NameMatch = NameMatchType.Contains, NameValue = "fattura" }
                    },
                    Action = RuleAction.Move,
                    DestinationTemplate = "Documenti\\Fatture"
                },
                new CustomRule
                {
                    Name = "Screenshot",
                    Order = 1,
                    Logic = ConditionLogic.Or,
                    Conditions = new()
                    {
                        new() { NameMatch = NameMatchType.StartsWith, NameValue = "Screenshot" }
                    },
                    Action = RuleAction.Move,
                    DestinationTemplate = "Immagini\\Screenshot"
                },
                new CustomRule
                {
                    Name = "Installer",
                    Order = 2,
                    Logic = ConditionLogic.And,
                    Conditions = new()
                    {
                        new() { Extensions = new() { ".exe" } }
                    },
                    Action = RuleAction.Move,
                    DestinationTemplate = "Installer"
                }
            }
        };

        var orgService = new OrganizationService(_safe, _categories, _ruleEngine, _journal);

        // Preview test
        var preview = orgService.Preview(profile);
        Assert.Equal(4, preview.Count);
        Assert.Contains(preview, p => p.RuleOrCategory == "Regola: Fatture PDF");
        Assert.Contains(preview, p => p.RuleOrCategory == "Regola: Screenshot");
        Assert.Contains(preview, p => p.RuleOrCategory == "Regola: Installer");
        Assert.Contains(preview, p => p.RuleOrCategory == "Categoria: Audio");

        // Actual run test
        var runId = "test_fase2_" + Guid.NewGuid().ToString("N")[..8];
        var (moved, errors) = orgService.Run(profile, runId, CancellationToken.None);

        Assert.Equal(4, moved);
        Assert.Equal(0, errors);

        // Verify files moved to expected destinations
        Assert.True(File.Exists(Path.Combine(destDir, "Documenti", "Fatture", "fattura_luce.pdf")));
        Assert.True(File.Exists(Path.Combine(destDir, "Immagini", "Screenshot", "Screenshot_2026.png")));
        Assert.True(File.Exists(Path.Combine(destDir, "Installer", "setup.exe")));
        Assert.True(File.Exists(Path.Combine(destDir, "Audio", "canzone.mp3")));

        // Test Undo
        var undoResults = _journal.UndoRun(runId, _safe);
        Assert.Equal(4, undoResults.Count);
        Assert.All(undoResults, u => Assert.True(u.Success));

        // Files are back in source!
        Assert.True(File.Exists(f1));
        Assert.True(File.Exists(f2));
        Assert.True(File.Exists(f3));
        Assert.True(File.Exists(f4));
    }

    [Fact]
    public void SchedulerService_Tick_FiresWhenDue()
    {
        var scheduler = new SchedulerService();
        var triggered = false;

        var profile = new Profile
        {
            Id = "p_test",
            Name = "Pianificato",
            Enabled = true,
            Schedule = new()
            {
                Enabled = true,
                Trigger = ScheduleTrigger.EveryNHours,
                IntervalHours = 1
            }
        };

        scheduler.ProfileDue += id =>
        {
            if (id == "p_test") triggered = true;
        };

        // First tick: has not run before -> triggers
        scheduler.Tick(new[] { profile });
        Assert.True(triggered);

        // Second tick immediately after: not due yet -> does not trigger
        triggered = false;
        scheduler.Tick(new[] { profile });
        Assert.False(triggered);
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CosmoTest_Fase2_" + Guid.NewGuid().ToString("N")[..8]);
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
