// CosmOrganizer.Core - JournalSearchService
// Search history and find where a file was moved ("Dov'è finito?").
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

public class RunHistorySummary
{
    public string RunId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public int TotalOperations { get; set; }
    public int MovedCount { get; set; }
    public int CompressedCount { get; set; }
    public int RecycledCount { get; set; }
}

public class WhereDidItGoResult
{
    public string FileName { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string OriginalSource { get; init; } = string.Empty;
    public string FinalDestination { get; init; } = string.Empty;
    public bool DestinationStillExists { get; init; }
}

public class JournalSearchService
{
    private readonly JournalService _journal;

    public JournalSearchService(JournalService journal)
    {
        _journal = journal;
    }

    public List<RunHistorySummary> GetRunsHistory()
    {
        var runIds = _journal.GetAllRunIds();
        var summaries = new List<RunHistorySummary>();

        foreach (var runId in runIds)
        {
            var ops = _journal.LoadRun(runId).ToList();
            if (ops.Count == 0) continue;

            var summary = new RunHistorySummary
            {
                RunId = runId,
                Timestamp = ops.FirstOrDefault()?.Timestamp ?? DateTime.MinValue,
                TotalOperations = ops.Count,
                MovedCount = ops.Count(o => o.Kind == "moved" || o.Kind == "copied"),
                CompressedCount = ops.Count(o => o.Kind == "compressed"),
                RecycledCount = ops.Count(o => o.Kind == "recycled")
            };
            summaries.Add(summary);
        }

        return summaries.OrderByDescending(s => s.Timestamp).ToList();
    }

    /// <summary>
    /// Searches journal across all runs for operations involving <paramref name="fileNameQuery"/>.
    /// </summary>
    public List<WhereDidItGoResult> FindFile(string fileNameQuery)
    {
        var results = new List<WhereDidItGoResult>();
        if (string.IsNullOrWhiteSpace(fileNameQuery)) return results;

        var runIds = _journal.GetAllRunIds();
        foreach (var runId in runIds)
        {
            var ops = _journal.LoadRun(runId);
            foreach (var op in ops)
            {
                var srcName = Path.GetFileName(op.Source);
                var dstName = Path.GetFileName(op.Dest);

                if (srcName.Contains(fileNameQuery, StringComparison.OrdinalIgnoreCase) ||
                    dstName.Contains(fileNameQuery, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new WhereDidItGoResult
                    {
                        FileName = srcName,
                        RunId = op.RunId,
                        Timestamp = op.Timestamp,
                        Kind = op.Kind,
                        OriginalSource = op.Source,
                        FinalDestination = op.Dest,
                        DestinationStillExists = !string.IsNullOrEmpty(op.Dest) && File.Exists(op.Dest)
                    });
                }
            }
        }

        return results.OrderByDescending(r => r.Timestamp).ToList();
    }
}
