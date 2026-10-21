using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MESOL.Server.Catalog;

/// <summary>One catalog entry: a race, subtype, place, birthsign, faction, rank and so on.</summary>
public sealed class CatalogEntry
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Race name ("Khajiit") for race-specific entries, "Mixed" for mixed ancestry, null for shared ones.</summary>
    public string? Race { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Parent { get; set; }

    public string Summary { get; set; } = string.Empty;

    /// <summary>"current" (valid in 3E 433), "historical" (heritage only) or "future" (never).</summary>
    public string Era { get; set; } = "current";

    /// <summary>"plausible", "implausible" or "n/a": does it fit the single Oblivion player model.</summary>
    public string? ModelFit { get; set; }

    public bool RemasteredOrigin { get; set; }

    public List<string> Sources { get; set; } = new();

    public bool IsFuture => string.Equals(Era, "future", StringComparison.OrdinalIgnoreCase);

    public bool IsHistorical => string.Equals(Era, "historical", StringComparison.OrdinalIgnoreCase);

    public bool IsImplausible => string.Equals(ModelFit, "implausible", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The lore catalog: the shipped file plus server owners' additions. Owners' files are either a list of entries,
/// or <c>{ "entries": [...], "hide": ["id", ...] }</c>; an entry with an existing id replaces it.
/// </summary>
public sealed class LoreCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, CatalogEntry> _byId;

    private LoreCatalog(Dictionary<string, CatalogEntry> byId)
    {
        _byId = byId;
    }

    public int Count => _byId.Count;

    public IEnumerable<CatalogEntry> Entries => _byId.Values;

    public static LoreCatalog Empty { get; } = new(new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the catalog from the shipped file and any additions, in order. Throws <see cref="FormatException"/>
    /// with a readable reason when a file is not valid; <paramref name="warnings"/> collects entries that were
    /// skipped (no id or name, unknown era).
    /// </summary>
    public static LoreCatalog Load(string shippedJson, IEnumerable<(string Name, string Json)> additions, List<string> warnings)
    {
        Dictionary<string, CatalogEntry> byId = new(StringComparer.OrdinalIgnoreCase);
        Add(byId, Parse("mesol-lore.json", shippedJson, out _), "mesol-lore.json", warnings);
        foreach ((string name, string json) in additions)
        {
            List<CatalogEntry> entries = Parse(name, json, out List<string> hide);
            Add(byId, entries, name, warnings);
            foreach (string id in hide)
            {
                if (!byId.Remove(id))
                {
                    warnings.Add($"{name}: cannot hide '{id}', no such entry.");
                }
            }
        }
        return new LoreCatalog(byId);
    }

    public bool TryGet(string? id, out CatalogEntry entry)
    {
        if (id is not null && _byId.TryGetValue(id, out CatalogEntry? found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>The display name of an entry id, or the id itself when the entry is gone (hidden by the owner).</summary>
    public string NameOf(string id) => TryGet(id, out CatalogEntry entry) ? entry.Name : id;

    /// <summary>
    /// Entries of the given kinds whose name or id contains <paramref name="query"/>, best match first
    /// (exact name, then name starting with the query, then the rest by name).
    /// </summary>
    public List<CatalogEntry> Search(IReadOnlyCollection<string> kinds, string query, Func<CatalogEntry, bool> allowed, int max)
    {
        string q = (query ?? string.Empty).Trim();
        return _byId.Values
            .Where(e => kinds.Contains(e.Kind, StringComparer.OrdinalIgnoreCase) && allowed(e))
            .Where(e => q.Length == 0
                || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => string.Equals(e.Name, q, StringComparison.OrdinalIgnoreCase) ? 0
                : e.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(e => e.RemasteredOrigin ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static List<CatalogEntry> Parse(string name, string json, out List<string> hide)
    {
        hide = new List<string>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            JsonElement root = document.RootElement;
            JsonElement entries = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!TryProperty(root, "entries", out entries))
                {
                    entries = default;
                }
                if (TryProperty(root, "hide", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.Array)
                {
                    hide.AddRange(hidden.EnumerateArray().Select(h => h.GetString()).OfType<string>());
                }
            }
            if (entries.ValueKind == JsonValueKind.Undefined)
            {
                return new List<CatalogEntry>();
            }
            if (entries.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"{name}: expected a list of entries or {{ \"entries\": [...] }}.");
            }
            return entries.Deserialize<List<CatalogEntry>>(Json) ?? new List<CatalogEntry>();
        }
        catch (JsonException exception)
        {
            throw new FormatException($"{name}: not valid JSON ({exception.Message}).", exception);
        }
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static void Add(Dictionary<string, CatalogEntry> byId, List<CatalogEntry> entries, string file, List<string> warnings)
    {
        foreach (CatalogEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Kind))
            {
                warnings.Add($"{file}: skipped an entry without id, name or kind ({entry.Id}).");
                continue;
            }
            if (entry.Era is not ("current" or "historical" or "future"))
            {
                warnings.Add($"{file}: skipped '{entry.Id}', unknown era '{entry.Era}'.");
                continue;
            }
            byId[entry.Id] = entry;
        }
    }
}
