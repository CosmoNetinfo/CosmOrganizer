// CosmOrganizer.Core - CategoryService
// Loads categories from categories.json and resolves file → category.
// Falls back to built-in defaults if the file is missing or corrupt.
// © CosmoNet.info — MIT License

using System.Text.Json;

namespace CosmOrganizer.Core;

/// <summary>
/// Resolves a file extension to its category name.
/// Categories are loaded from categories.json (user-editable).
/// The last category with empty Extensions list is the catch-all ("Da smistare").
/// </summary>
public class CategoryService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private List<CategoryDefinition> _categories = new();
    private readonly Dictionary<string, string> _extToCategory = new(StringComparer.OrdinalIgnoreCase);
    private string? _catchAll;

    // ---------------------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Loads categories from the specified JSON file path.
    /// If the file is missing or corrupt, built-in defaults are used.
    /// </summary>
    public void Load(string categoriesJsonPath)
    {
        List<CategoryDefinition>? loaded = null;
        if (File.Exists(categoriesJsonPath))
        {
            try
            {
                var json = File.ReadAllText(categoriesJsonPath);
                loaded = JsonSerializer.Deserialize<List<CategoryDefinition>>(json, JsonOpts);
            }
            catch { /* fall through to defaults */ }
        }

        _categories = loaded?.Count > 0 ? loaded : GetBuiltInDefaults();
        RebuildIndex();
    }

    /// <summary>
    /// Loads categories using the app's own directory (for production).
    /// </summary>
    public void LoadFromAppDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Interface", "categories.json");
        Load(path);
    }

    // ---------------------------------------------------------------------------
    // Resolve
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the category name for a given file path.
    /// Falls back to the catch-all category ("Da smistare") if no match.
    /// </summary>
    public string GetCategory(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && _extToCategory.TryGetValue(ext, out var cat))
            return cat;
        return _catchAll ?? "Da smistare";
    }

    /// <summary>Returns the loaded category list (for UI display).</summary>
    public IReadOnlyList<CategoryDefinition> Categories => _categories;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private void RebuildIndex()
    {
        _extToCategory.Clear();
        _catchAll = null;

        foreach (var cat in _categories)
        {
            if (cat.Extensions.Count == 0)
            {
                // First empty-extension category becomes the catch-all
                _catchAll ??= cat.Name;
                continue;
            }
            foreach (var ext in cat.Extensions)
            {
                var normalized = ext.StartsWith('.') ? ext : "." + ext;
                _extToCategory.TryAdd(normalized, cat.Name);
            }
        }

        _catchAll ??= "Da smistare";
    }

    private static List<CategoryDefinition> GetBuiltInDefaults() => new()
    {
        new() { Name = "Immagini",      Extensions = new() { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".heic", ".psd", ".raw", ".avif" } },
        new() { Name = "Video",         Extensions = new() { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp" } },
        new() { Name = "Audio",         Extensions = new() { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus" } },
        new() { Name = "Documenti",     Extensions = new() { ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".epub" } },
        new() { Name = "Fogli",         Extensions = new() { ".xls", ".xlsx", ".csv", ".ods" } },
        new() { Name = "Presentazioni", Extensions = new() { ".ppt", ".pptx", ".odp" } },
        new() { Name = "Archivi",       Extensions = new() { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso" } },
        new() { Name = "Installer",     Extensions = new() { ".exe", ".msi", ".msix", ".appx" } },
        new() { Name = "Sviluppo",      Extensions = new() { ".py", ".js", ".ts", ".cs", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sh", ".bat", ".ps1", ".sql" } },
        new() { Name = "Font",          Extensions = new() { ".ttf", ".otf", ".woff", ".woff2" } },
        new() { Name = "Da smistare",   Extensions = new() },
    };
}
