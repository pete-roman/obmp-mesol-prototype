using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Catalog;

/// <summary>
/// Loads the lore catalog when the server starts: <c>server/catalog/mesol-lore.json</c> from the mod, then the
/// owner's <c>catalog.d/*.json</c> from the data folder, in name order. If the shipped file is missing or broken,
/// MESOL still runs with an empty catalog (free text only) and says so in the log.
/// </summary>
[Service]
public sealed partial class CatalogService(MesolData data, ILogger logger)
{
    public LoreCatalog Current { get; private set; } = LoreCatalog.Empty;

    /// <summary>Why the catalog is empty, for players' error messages; null when it loaded.</summary>
    public string? Problem { get; private set; } = "The lore catalog is still loading.";

    /// <summary>For the tests: use this catalog instead of loading files.</summary>
    internal void Use(LoreCatalog catalog)
    {
        Current = catalog;
        Problem = null;
    }

    private void Start()
    {
        string shipped = Path.Combine(data.ModFolder, "catalog", "mesol-lore.json");
        try
        {
            List<(string, string)> additions = new();
            if (Directory.Exists(data.CatalogAdditions))
            {
                foreach (string file in Directory.GetFiles(data.CatalogAdditions, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    additions.Add((Path.GetFileName(file), File.ReadAllText(file)));
                }
            }
            List<string> warnings = new();
            Current = LoreCatalog.Load(File.ReadAllText(shipped), additions, warnings);
            Problem = null;
            foreach (string warning in warnings)
            {
                logger.LogWarning("MESOL: catalog: {Warning}", warning);
            }
            logger.LogInformation("MESOL: lore catalog loaded, {Count} entries ({Additions} owner file(s) from {Folder}).",
                Current.Count, additions.Count, data.CatalogAdditions);
        }
        catch (Exception exception)
        {
            Problem = "The lore catalog did not load, so only free text works for now.";
            logger.LogError(exception, "MESOL: could not load the lore catalog from {File}.", shipped);
        }
    }
}
