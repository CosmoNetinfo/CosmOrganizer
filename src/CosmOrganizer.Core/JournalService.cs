// CosmOrganizer.Core - JournalService
// Persistent per-run operation log: append, load, undo.
// Thread-safe via lock. © CosmoNet.info — MIT License

using System.Text.Json;

namespace CosmOrganizer.Core;

/// <summary>
/// Records every file operation to a per-run JSONL file under
/// %LOCALAPPDATA%\CosmOrganizer\journal\{runId}.jsonl.
/// Supports undo by replaying operations in reverse.
/// </summary>
public class JournalService
{
    private static readonly string JournalDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CosmOrganizer", "journal");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();

    // ---------------------------------------------------------------------------
    // Write
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Appends a <see cref="FileOperation"/> entry to the run's journal file.
    /// Thread-safe.
    /// </summary>
    public void AppendEntry(FileOperation op)
    {
        Directory.CreateDirectory(JournalDir);
        var filePath = GetJournalPath(op.RunId);
        var line = JsonSerializer.Serialize(op, JsonOpts) + Environment.NewLine;

        lock (_lock)
        {
            File.AppendAllText(filePath, line);
        }
    }

    // ---------------------------------------------------------------------------
    // Read
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Loads all operations recorded for <paramref name="runId"/>.
    /// </summary>
    public IEnumerable<FileOperation> LoadRun(string runId)
    {
        var filePath = GetJournalPath(runId);
        if (!File.Exists(filePath)) yield break;

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            FileOperation? op;
            try { op = JsonSerializer.Deserialize<FileOperation>(line, JsonOpts); }
            catch { continue; }
            if (op != null) yield return op;
        }
    }

    /// <summary>
    /// Returns all run IDs found in the journal directory (filename without extension).
    /// </summary>
    public IEnumerable<string> GetAllRunIds()
    {
        if (!Directory.Exists(JournalDir)) return Enumerable.Empty<string>();

        return Directory
            .EnumerateFiles(JournalDir, "*.jsonl")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderByDescending(id => id); // most recent first (ISO-sortable IDs)
    }

    // ---------------------------------------------------------------------------
    // Undo
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Replays operations of a run in reverse order to undo them.
    /// - "moved"      → move file back from Dest to Source
    /// - "copied"     → delete the copy (Recycle Bin)
    /// - "compressed" → extract zip to original path, then Recycle the zip
    /// - "recycled"   → cannot undo; logs a warning
    /// </summary>
    /// <returns>List of (operation, success, message) tuples.</returns>
    public List<(FileOperation Op, bool Success, string Message)> UndoRun(
        string runId,
        SafeFileService safeFile)
    {
        var ops = LoadRun(runId).Reverse().ToList();
        var results = new List<(FileOperation, bool, string)>();

        foreach (var op in ops)
        {
            try
            {
                switch (op.Kind.ToLowerInvariant())
                {
                    case "moved":
                        if (!File.Exists(op.Dest))
                        {
                            results.Add((op, false, $"File non trovato: {op.Dest}"));
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(op.Source)!);
                        safeFile.MoveFile(op.Dest, op.Source);
                        results.Add((op, true, "Ripristinato"));
                        break;

                    case "copied":
                        if (File.Exists(op.Dest))
                            safeFile.RecycleFile(op.Dest);
                        results.Add((op, true, "Copia rimossa"));
                        break;

                    case "compressed":
                        UndoCompressed(op, safeFile, results);
                        break;

                    case "recycled":
                        results.Add((op, false,
                            "Impossibile annullare: il file è nel Cestino"));
                        break;

                    default:
                        results.Add((op, false, $"Tipo operazione sconosciuto: {op.Kind}"));
                        break;
                }
            }
            catch (Exception ex)
            {
                results.Add((op, false, ex.Message));
            }
        }

        return results;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static void UndoCompressed(
        FileOperation op,
        SafeFileService safeFile,
        List<(FileOperation, bool, string)> results)
    {
        // Dest is the zip file, Source is the original file path
        if (!File.Exists(op.Dest))
        {
            results.Add((op, false, $"Archivio non trovato: {op.Dest}"));
            return;
        }

        try
        {
            var extractDir = Path.GetDirectoryName(op.Source)!;
            Directory.CreateDirectory(extractDir);

            System.IO.Compression.ZipFile.ExtractToDirectory(op.Dest, extractDir, overwriteFiles: false);

            // Recycle the zip after successful extraction
            safeFile.RecycleFile(op.Dest);
            results.Add((op, true, "Decompressa e archivio rimosso"));
        }
        catch (Exception ex)
        {
            results.Add((op, false, $"Errore decompressione: {ex.Message}"));
        }
    }

    private static string GetJournalPath(string runId)
        => Path.Combine(JournalDir, $"{runId}.jsonl");
}
