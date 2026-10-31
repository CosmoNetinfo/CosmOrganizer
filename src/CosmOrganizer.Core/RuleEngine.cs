// CosmOrganizer.Core - RuleEngine
// Evaluates CustomRules against a file and resolves destination path templates.
// Reads Zone.Identifier alternate data stream for download-origin conditions.
// © CosmoNet.info — MIT License

using System.Text.RegularExpressions;

namespace CosmOrganizer.Core;

/// <summary>Result of evaluating rules against a file.</summary>
public class RuleMatch
{
    /// <summary>The rule that matched, or null if no rule matched.</summary>
    public CustomRule? Rule { get; init; }

    /// <summary>Resolved destination subfolder (relative path or absolute).</summary>
    public string ResolvedDestination { get; init; } = string.Empty;

    /// <summary>Resolved new filename (for Rename action), or null.</summary>
    public string? ResolvedName { get; init; }
}

/// <summary>
/// Evaluates a file against an ordered list of <see cref="CustomRule"/> objects
/// and resolves path templates with placeholders.
/// </summary>
public class RuleEngine
{
    // ---------------------------------------------------------------------------
    // Main evaluation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the first matching rule result, or null if no rule matches.
    /// Rules are evaluated in ascending Order.
    /// </summary>
    public RuleMatch? Evaluate(string filePath, IReadOnlyList<CustomRule> rules)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return null;

        var zoneInfo = ReadZoneIdentifier(filePath);

        foreach (var rule in rules.Where(r => r.Enabled).OrderBy(r => r.Order))
        {
            if (Matches(filePath, info, zoneInfo, rule))
            {
                var dest  = ResolveTemplate(rule.DestinationTemplate, filePath, info);
                var name  = rule.RenameTemplate != null
                    ? ResolveTemplate(rule.RenameTemplate, filePath, info)
                    : null;
                return new RuleMatch { Rule = rule, ResolvedDestination = dest, ResolvedName = name };
            }
        }
        return null;
    }

    // ---------------------------------------------------------------------------
    // Condition evaluation
    // ---------------------------------------------------------------------------

    private static bool Matches(
        string filePath, FileInfo info,
        ZoneInfo zoneInfo, CustomRule rule)
    {
        if (rule.Conditions.Count == 0) return false;

        var results = rule.Conditions.Select(c => EvalCondition(filePath, info, zoneInfo, c));

        return rule.Logic == ConditionLogic.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    private static bool EvalCondition(
        string filePath, FileInfo info,
        ZoneInfo zoneInfo, RuleCondition cond)
    {
        bool allTrue = true;

        // Name-based condition
        if (cond.NameMatch.HasValue && !string.IsNullOrEmpty(cond.NameValue))
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            allTrue &= MatchName(name, cond.NameMatch.Value, cond.NameValue);
        }

        // Extension condition
        if (cond.Extensions is { Count: > 0 })
        {
            var ext = Path.GetExtension(filePath);
            allTrue &= cond.Extensions.Any(e =>
                string.Equals(e.StartsWith('.') ? e : "." + e, ext, StringComparison.OrdinalIgnoreCase));
        }

        // Size conditions
        if (cond.MinSizeBytes.HasValue)
            allTrue &= info.Length >= cond.MinSizeBytes.Value;
        if (cond.MaxSizeBytes.HasValue)
            allTrue &= info.Length <= cond.MaxSizeBytes.Value;

        // Age conditions
        if (cond.OlderThanDays.HasValue && cond.AgeBasis.HasValue)
        {
            var reference = cond.AgeBasis.Value == AgeBasis.Created
                ? info.CreationTime
                : info.LastWriteTime;
            allTrue &= (DateTime.Now - reference).TotalDays > cond.OlderThanDays.Value;
        }

        // Source folder condition
        if (!string.IsNullOrEmpty(cond.SourceFolder))
        {
            allTrue &= filePath.StartsWith(cond.SourceFolder, StringComparison.OrdinalIgnoreCase);
        }

        // Zone.Identifier (download origin)
        if (!string.IsNullOrEmpty(cond.DownloadSourceContains))
        {
            var combined = (zoneInfo.HostUrl + " " + zoneInfo.ReferrerUrl).ToLowerInvariant();
            allTrue &= combined.Contains(cond.DownloadSourceContains.ToLowerInvariant());
        }

        return allTrue;
    }

    private static bool MatchName(string name, NameMatchType matchType, string value)
    {
        return matchType switch
        {
            NameMatchType.Contains   => name.Contains(value, StringComparison.OrdinalIgnoreCase),
            NameMatchType.StartsWith => name.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            NameMatchType.EndsWith   => name.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            NameMatchType.Regex      => Regex.IsMatch(name, value, RegexOptions.IgnoreCase),
            _ => false
        };
    }

    // ---------------------------------------------------------------------------
    // Template resolution
    // ---------------------------------------------------------------------------

    private static readonly Regex PlaceholderRe = new(@"\{(\w+)\}", RegexOptions.Compiled);

    // Counter is per-session, shared across all resolutions (good enough for uniqueness)
    private int _counter;

    /// <summary>
    /// Resolves a destination or rename template by replacing known placeholders.
    /// Placeholders: {categoria} {ext} {anno} {mese} {giorno} {nome} {dominio} {contatore}
    /// </summary>
    public string ResolveTemplate(
        string template,
        string filePath,
        FileInfo? info = null,
        string? categoryOverride = null)
    {
        info ??= new FileInfo(filePath);
        var created = info.CreationTime;

        return PlaceholderRe.Replace(template, m =>
        {
            return m.Groups[1].Value.ToLowerInvariant() switch
            {
                "categoria" => categoryOverride ?? string.Empty,
                "ext"       => Path.GetExtension(filePath).TrimStart('.'),
                "anno"      => created.Year.ToString("D4"),
                "mese"      => created.Month.ToString("D2"),
                "giorno"    => created.Day.ToString("D2"),
                "nome"      => Path.GetFileNameWithoutExtension(filePath),
                "dominio"   => ReadZoneIdentifier(filePath).Domain,
                "contatore" => (++_counter).ToString("D4"),
                _           => m.Value  // unknown placeholder: keep as-is
            };
        });
    }

    // ---------------------------------------------------------------------------
    // Zone.Identifier (NTFS Alternate Data Stream)
    // ---------------------------------------------------------------------------

    private record ZoneInfo(string HostUrl, string ReferrerUrl, string Domain);

    private static ZoneInfo ReadZoneIdentifier(string filePath)
    {
        var adsPath = filePath + ":Zone.Identifier";
        try
        {
            if (!File.Exists(adsPath)) return new ZoneInfo(string.Empty, string.Empty, string.Empty);

            var lines = File.ReadAllLines(adsPath);
            string host = string.Empty, referrer = string.Empty;
            foreach (var line in lines)
            {
                if (line.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase))
                    host = line[8..].Trim();
                else if (line.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
                    referrer = line[12..].Trim();
            }

            // Extract domain from HostUrl
            string domain = string.Empty;
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
                domain = uri.Host;

            return new ZoneInfo(host, referrer, domain);
        }
        catch
        {
            return new ZoneInfo(string.Empty, string.Empty, string.Empty);
        }
    }
}
