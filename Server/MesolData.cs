using System.IO;
using ReadyM.SDK.Attributes;

namespace MESOL.Server;

/// <summary>
/// The folders MESOL reads and writes. Worked out on first use, because the mod's folder is only known once the
/// mod has loaded.
/// </summary>
[Service]
public sealed partial class MesolData(Mod mod, MesolConfig config)
{
    private string? _root;

    /// <summary><c>mods/MESOL/server</c>: the shipped catalog lives here.</summary>
    public string ModFolder => mod.ModDirectory;

    /// <summary>The data folder (cards, owner catalog additions). See <see cref="MesolConfig.DataFolder"/>.</summary>
    public string Root => _root ??= Resolve(mod.ModDirectory, config.DataFolder);

    public string Cards => Path.Combine(Root, "cards");

    /// <summary>Who knows whom: introductions, friendships and blocks, one file per ReadyM account.</summary>
    public string Relations => Path.Combine(Root, "relations");

    /// <summary>Server owners' own catalog files: extra entries, or entries to hide.</summary>
    public string CatalogAdditions => Path.Combine(Root, "catalog.d");

    /// <summary>
    /// The configured folder, or <c>&lt;server root&gt;/moddata/MESOL</c>. The mod folder is
    /// <c>&lt;server root&gt;/mods/MESOL/server</c>, so the server root is three levels up. A relative configured
    /// folder is taken from the server root too.
    /// </summary>
    internal static string Resolve(string modDirectory, string configured)
    {
        string serverRoot = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", ".."));
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(serverRoot, "moddata", "MESOL");
        }
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(serverRoot, configured));
    }
}
