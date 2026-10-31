// CosmOrganizer.Core - Category models
// Loaded from categories.json (user-editable).
// © CosmoNet.info — MIT License

using System.Text.Json.Serialization;

namespace CosmOrganizer.Core;

/// <summary>One category row from categories.json.</summary>
public class CategoryDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("extensions")]
    public List<string> Extensions { get; set; } = new();
}
