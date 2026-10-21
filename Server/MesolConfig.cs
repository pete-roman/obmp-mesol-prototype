using ReadyM.SDK.Attributes;

namespace MESOL.Server;

/// <summary>
/// Server settings, read once at start from <c>server/config.json</c> (never sent to players).
/// A missing file keeps these defaults; an unknown key stops the mod from loading (SDK rule), so a typo is not silent.
/// </summary>
[ModConfig]
public sealed partial class MesolConfig
{
    /// <summary>
    /// Where cards are saved. Empty means <c>&lt;server root&gt;/moddata/MESOL</c>, outside the mod folder,
    /// so replacing the mod folder with a new version never deletes cards.
    /// </summary>
    public string DataFolder { get; set; } = string.Empty;

    /// <summary>
    /// How strict the lore catalog is: "canon" (race, places, factions and the other catalog fields are picked
    /// from the catalog, never typed), "canonPlus" (catalog plus free text, marked custom) or "free" (free text
    /// anywhere, catalog as suggestions). Names, looks and stories are free text in every mode.
    /// </summary>
    public string CatalogMode { get; set; } = "canon";

    /// <summary>Offer subtypes that do not fit the Oblivion player model (for example Senche or Naga).</summary>
    public bool AllowImplausibleSubtypes { get; set; }

    /// <summary>
    /// When true and the game reports the character's race, the card's race must match it.
    /// Off by default: on OB:MP 1.0.0-rc.2 the server does not save race yet (known issue).
    /// </summary>
    public bool RaceLock { get; set; }

    /// <summary>How far /lore without an id looks for the player in front of you, in metres.</summary>
    public float LookRangeMeters { get; set; } = 5f;
}
