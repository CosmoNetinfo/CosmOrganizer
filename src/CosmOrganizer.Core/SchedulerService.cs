// CosmOrganizer.Core - SchedulerService
// Internal scheduler — no Windows Task Scheduler dependency.
// Fires profile runs based on ScheduleTrigger settings.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

/// <summary>
/// Checks every minute whether a profile's schedule should fire.
/// Call <see cref="Tick"/> from a recurring timer on the UI thread (or any thread).
/// </summary>
public class SchedulerService : IDisposable
{
    private readonly Dictionary<string, DateTime> _lastRun = new();
    private readonly object _lock = new();
    private bool _disposed;

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Raised when a profile's schedule fires.
    /// The string argument is the profile ID.
    /// </summary>
    public event Action<string>? ProfileDue;

    /// <summary>
    /// Call this every minute. Evaluates each enabled profile's schedule.
    /// </summary>
    public void Tick(IReadOnlyList<Profile> profiles)
    {
        var now = DateTime.Now;
        foreach (var profile in profiles)
        {
            if (!profile.Enabled || !profile.Schedule.Enabled) continue;
            if (IsDue(profile, now))
            {
                lock (_lock) { _lastRun[profile.Id] = now; }
                ProfileDue?.Invoke(profile.Id);
            }
        }
    }

    /// <summary>Records that a profile just ran (updates last-run time).</summary>
    public void MarkRan(string profileId)
    {
        lock (_lock) { _lastRun[profileId] = DateTime.Now; }
    }

    // ---------------------------------------------------------------------------
    // OnStartup trigger — call once at app start
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns profiles that have OnStartup trigger and should run at startup.
    /// </summary>
    public IEnumerable<Profile> GetStartupProfiles(IReadOnlyList<Profile> profiles)
        => profiles.Where(p => p.Enabled
            && p.Schedule.Enabled
            && p.Schedule.Trigger == ScheduleTrigger.OnStartup);

    // ---------------------------------------------------------------------------
    // FolderThreshold check
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns profiles whose source folder has exceeded the configured threshold.
    /// </summary>
    public IEnumerable<Profile> GetThresholdBreached(IReadOnlyList<Profile> profiles)
    {
        foreach (var profile in profiles)
        {
            if (!profile.Enabled || !profile.Schedule.Enabled) continue;
            if (profile.Schedule.Trigger != ScheduleTrigger.FolderThreshold) continue;

            if (IsThresholdBreached(profile))
                yield return profile;
        }
    }

    private static bool IsThresholdBreached(Profile profile)
    {
        foreach (var src in profile.SourceFolders)
        {
            if (!Directory.Exists(src)) continue;
            try
            {
                if (profile.Schedule.ThresholdFiles > 0)
                {
                    int count = Directory.EnumerateFiles(src).Count();
                    if (count >= profile.Schedule.ThresholdFiles) return true;
                }
                if (profile.Schedule.ThresholdMB > 0)
                {
                    long bytes = 0;
                    foreach (var f in Directory.EnumerateFiles(src))
                    {
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
                    if (bytes / 1_048_576 >= profile.Schedule.ThresholdMB) return true;
                }
            }
            catch { }
        }
        return false;
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private bool IsDue(Profile profile, DateTime now)
    {
        var s = profile.Schedule;
        lock (_lock) { _lastRun.TryGetValue(profile.Id, out var last); }

        _lastRun.TryGetValue(profile.Id, out var lastRun);

        return s.Trigger switch
        {
            ScheduleTrigger.EveryNHours =>
                s.IntervalHours > 0 &&
                (now - lastRun).TotalHours >= s.IntervalHours,

            ScheduleTrigger.DailyAt =>
                now.TimeOfDay >= s.DailyAt &&
                (lastRun.Date < now.Date || lastRun == default),

            // OnStartup and FolderThreshold are handled separately
            _ => false
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
