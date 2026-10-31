using System;
using System.IO;
using CosmOrganizer.Core;
using Xunit;

namespace CosmOrganizer.Tests;

public class Fase4Tests : IDisposable
{
    private readonly string _testDir;

    public Fase4Tests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CosmOrganizer_Fase4Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
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
    public void AppSettings_WindowPosition_DefaultAndPersistence()
    {
        var service = new SettingsService(_testDir);
        var (settings, wasCorrupted) = service.Load();

        Assert.False(wasCorrupted);
        // Default window position values
        Assert.Equal(0, settings.WindowLeft);
        Assert.Equal(0, settings.WindowTop);
        Assert.Equal(0, settings.WindowWidth);
        Assert.Equal(0, settings.WindowHeight);
        Assert.False(settings.IsMaximized);
        Assert.True(settings.AutoUpdateEnabled);

        // Modify and save
        settings.WindowLeft = 120;
        settings.WindowTop = 80;
        settings.WindowWidth = 1280;
        settings.WindowHeight = 800;
        settings.IsMaximized = true;
        settings.AutoUpdateEnabled = false;

        service.Save(settings);

        // Reload
        var reloadedService = new SettingsService(_testDir);
        var (reloaded, reloadedCorrupted) = reloadedService.Load();

        Assert.False(reloadedCorrupted);
        Assert.Equal(120, reloaded.WindowLeft);
        Assert.Equal(80, reloaded.WindowTop);
        Assert.Equal(1280, reloaded.WindowWidth);
        Assert.Equal(800, reloaded.WindowHeight);
        Assert.True(reloaded.IsMaximized);
        Assert.False(reloaded.AutoUpdateEnabled);
    }

    [Fact]
    public void AppSettings_PreservesOtherProperties_WhenWindowChanges()
    {
        var service = new SettingsService(_testDir);
        var (settings, _) = service.Load();
        settings.IncludeSubfolders = true;
        settings.DeleteEmptyFolders = true;
        settings.WindowWidth = 1024;
        service.Save(settings);

        var (reloaded, _) = service.Load();
        Assert.True(reloaded.IncludeSubfolders);
        Assert.True(reloaded.DeleteEmptyFolders);
        Assert.Equal(1024, reloaded.WindowWidth);
    }
}
