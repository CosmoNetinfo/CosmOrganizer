// CosmOrganizer.Core - FileOperation record
// Represents a single file operation recorded in the journal.
// © CosmoNet.info — MIT License

namespace CosmOrganizer.Core;

/// <summary>
/// Immutable record of a single file operation performed during a run.
/// Kind values: "moved", "copied", "compressed", "recycled"
/// </summary>
public record FileOperation(
    string RunId,
    DateTime Timestamp,
    string Kind,
    string Source,
    string Dest);
