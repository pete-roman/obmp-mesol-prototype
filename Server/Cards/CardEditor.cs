using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MESOL.Server.Catalog;

namespace MESOL.Server.Cards;

/// <summary>How strict the catalog is (<see cref="MesolConfig.CatalogMode"/>).</summary>
public enum CatalogMode
{
    /// <summary>Catalog fields take catalog picks only.</summary>
    Canon,

    /// <summary>Free text is allowed next to or instead of a pick, and marked custom.</summary>
    CanonPlus,

    /// <summary>Free text anywhere, nothing marked.</summary>
    Free,
}

public sealed record EditResult(bool Ok, string Message)
{
    public static EditResult Fail(string message) => new(false, message);

    public static EditResult Done(string message) => new(true, message);
}

/// <summary>
/// Every rule for changing a card: lengths, choices, the catalog mode, the era rule (3E 433) and model fit.
/// No SDK types, so the tests run it directly. Callers save the card when a result is <see cref="EditResult.Ok"/>.
/// </summary>
public sealed class CardEditor(LoreCatalog catalog, CatalogMode mode, bool allowImplausible, Func<DateTimeOffset> clock)
{
    public const int MaxPicks = 8;

    public static CatalogMode ParseMode(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "canonplus" => CatalogMode.CanonPlus,
        "free" => CatalogMode.Free,
        _ => CatalogMode.Canon,
    };

    /// <summary>
    /// On a canon server, catalog fields hold catalog picks only: text typed there (before the server went canon)
    /// is removed and the visibility choice stays. True when something was removed; the caller saves the card.
    /// </summary>
    public bool DropCustom(CardFile card)
    {
        if (mode != CatalogMode.Canon)
        {
            return false;
        }
        bool changed = false;
        foreach (FieldDefinition field in CardFields.All.Where(f => f.Type == FieldType.Catalog))
        {
            if (card.Get(field) is { } value && !string.IsNullOrEmpty(value.Text))
            {
                card.Put(field, new FieldValue { CatalogId = value.CatalogId, Visibility = value.Visibility }, clock());
                changed = true;
            }
        }
        return changed;
    }

    public EditResult Set(CardFile card, FieldDefinition field, string text)
    {
        string clean = Clean(text, field.Type == FieldType.LongText);
        if (clean.Length == 0)
        {
            return EditResult.Fail($"Nothing to set. To empty {field.Label}: /mylore clear {field.Id}");
        }
        FieldValue? old = card.Get(field);
        FieldValue value = new() { Visibility = old?.Visibility ?? field.DefaultVisibility };

        switch (field.Type)
        {
            case FieldType.Trait:
                return SetTrait(card, field, old, clean);
            case FieldType.Choice:
                string? choice = field.Choices!.FirstOrDefault(c => string.Equals(c, clean, StringComparison.OrdinalIgnoreCase));
                if (choice is null)
                {
                    return EditResult.Fail($"{field.Label} is one of: {string.Join(", ", field.Choices!)}.");
                }
                value.Text = choice;
                break;
            case FieldType.Catalog:
                if (mode == CatalogMode.Canon)
                {
                    return EditResult.Fail($"This server takes {field.Label} from the lore catalog only: /mylore pick {field.Id} <search>");
                }
                // Next to a pick, text is an addition ("Anequina" + "Rimmen outskirts"); alone it stands in for one.
                value.CatalogId = old?.CatalogId;
                value.Text = clean;
                value.NonCanon = value.CatalogId is null && mode == CatalogMode.CanonPlus;
                break;
            default:
                value.Text = clean;
                break;
        }
        return Store(card, field, old, value, $"{field.Label} set");
    }

    /// <summary>Adds text to the end of a long field, so it can be written in pieces under chat's line limit.</summary>
    public EditResult Append(CardFile card, FieldDefinition field, string text)
    {
        if (field.Type != FieldType.LongText)
        {
            return EditResult.Fail($"Only long fields take /mylore add. For {field.Label} use /mylore set {field.Id} <text>");
        }
        string clean = Clean(text, multiline: true);
        if (clean.Length == 0)
        {
            return EditResult.Fail("Nothing to add.");
        }
        FieldValue? old = card.Get(field);
        string joined = string.IsNullOrEmpty(old?.Text) ? clean : old!.Text + " " + clean;
        FieldValue value = new() { Text = joined, Visibility = old?.Visibility ?? field.DefaultVisibility };
        return Store(card, field, old, value, $"{field.Label} now has {joined.Length} of {field.MaxLength} characters");
    }

    public EditResult Clear(CardFile card, FieldDefinition field)
    {
        FieldValue? old = card.Get(field);
        if (old is null || old.IsEmpty)
        {
            return EditResult.Fail($"{field.Label} is already empty.");
        }
        if (field.Type == FieldType.Trait)
        {
            return EditResult.Fail($"{field.Label} is set for good. Only staff can change a trait.");
        }
        // Keep the owner's visibility choice for when the field is filled again.
        card.Put(field, new FieldValue { Visibility = old.Visibility }, clock());
        return EditResult.Done($"{field.Label} cleared.");
    }

    public EditResult SetVisibility(CardFile card, FieldDefinition field, Visibility visibility)
    {
        if (field.VisibilityLocked)
        {
            return EditResult.Fail($"{field.Label} is always {ViewerLevel.Word(field.DefaultVisibility)}.");
        }
        FieldValue value = card.Get(field) is { } old ? old.WithVisibility(visibility) : new FieldValue { Visibility = visibility };
        card.Put(field, value, clock());
        return EditResult.Done($"{field.Label} is now {ViewerLevel.Word(visibility)}.");
    }

    // ---- traits: typed once, or rolled with one reroll; then fixed for good ----

    /// <summary>A typed trait value. It is fixed at once: the player chose it.</summary>
    private EditResult SetTrait(CardFile card, FieldDefinition field, FieldValue? old, string text)
    {
        if (old?.Number is not null)
        {
            return EditResult.Fail(old.Fixed
                ? $"{field.Label} is set for good."
                : $"{field.Label} was rolled. Reroll it once, or keep it.");
        }
        if (!int.TryParse(text, out int number) || number < CardFields.TraitMin || number > CardFields.TraitMax)
        {
            return EditResult.Fail($"{field.Label} takes a number from {CardFields.TraitMin} to {CardFields.TraitMax}.");
        }
        card.Put(field, new FieldValue { Number = number, Fixed = true, Visibility = old?.Visibility ?? field.DefaultVisibility }, clock());
        return EditResult.Done($"{field.Label} is {number} ({CardFields.TraitWord(number)}), set for good.");
    }

    /// <summary>Whether the trait may be rolled now: never set (first roll) or rolled once and not kept (the reroll).</summary>
    public EditResult CanRoll(CardFile card, FieldDefinition field)
    {
        if (field.Type != FieldType.Trait)
        {
            return EditResult.Fail($"{field.Label} is not a trait.");
        }
        FieldValue? old = card.Get(field);
        if (old?.Number is null)
        {
            return EditResult.Done("first");
        }
        if (!old.Fixed && (old.Rolls?.Count ?? 0) == 1)
        {
            return EditResult.Done("reroll");
        }
        return EditResult.Fail(old.Rolls is { Count: > 0 }
            ? $"{field.Label} is set for good: no more rerolls."
            : $"{field.Label} was typed in and is set for good.");
    }

    /// <summary>
    /// Stores a roll the server made (RpRoll, 5d20). The first roll stays open for one reroll; the reroll is final.
    /// Call <see cref="CanRoll"/> first, so no roll is thrown away.
    /// </summary>
    public EditResult ApplyRoll(CardFile card, FieldDefinition field, int[] dice)
    {
        EditResult allowed = CanRoll(card, field);
        if (!allowed.Ok)
        {
            return allowed;
        }
        FieldValue? old = card.Get(field);
        int total = dice.Sum();
        if (dice.Length != CardFields.TraitDice || total < CardFields.TraitMin || total > CardFields.TraitMax)
        {
            return EditResult.Fail("That roll didn't work. Try again.");
        }
        bool reroll = allowed.Message == "reroll";
        List<TraitRoll> rolls = new(old?.Rolls ?? new List<TraitRoll>())
        {
            new TraitRoll { Dice = dice.ToArray(), Total = total, At = clock() },
        };
        card.Put(field, new FieldValue
        {
            Number = total,
            Fixed = reroll,
            Rolls = rolls,
            Visibility = old?.Visibility ?? field.DefaultVisibility,
        }, clock());
        return EditResult.Done(reroll
            ? $"{field.Label}: rerolled {total} ({CardFields.TraitWord(total)}), set for good."
            : $"{field.Label}: rolled {total} ({CardFields.TraitWord(total)}). Keep it, or reroll once.");
    }

    /// <summary>Keeps a first roll for good, giving up the reroll.</summary>
    public EditResult Keep(CardFile card, FieldDefinition field)
    {
        FieldValue? old = card.Get(field);
        if (field.Type != FieldType.Trait || old?.Number is null || old.Fixed)
        {
            return EditResult.Fail(old?.Fixed == true ? $"{field.Label} is already set for good." : $"{field.Label} has no roll to keep.");
        }
        FieldValue kept = old.WithVisibility(old.Visibility);
        kept.Fixed = true;
        card.Put(field, kept, clock());
        return EditResult.Done($"{field.Label} kept at {old.Number}, set for good.");
    }

    /// <summary>Catalog entries this card may pick for the field, matching the search (at most <paramref name="max"/>).</summary>
    public List<CatalogEntry> Candidates(CardFile card, FieldDefinition field, string query, out string? problem, int max = MaxPicks)
    {
        problem = null;
        if (field.Type != FieldType.Catalog)
        {
            problem = $"{field.Label} is not picked from the catalog. Use /mylore set {field.Id} <text>";
            return new List<CatalogEntry>();
        }
        string? race = RaceName(card);
        if (field.RaceScoped && race is null && field.Id is "subtype" or "culture")
        {
            problem = "Pick your race first: /mylore pick race <name>";
            return new List<CatalogEntry>();
        }
        List<CatalogEntry> found = catalog.Search(field.CatalogKinds!, query, e => Allowed(card, field, e, race), max);
        if (found.Count == 0)
        {
            problem = mode == CatalogMode.Canon
                ? $"Nothing in the catalog matches '{query}' for {field.Label}."
                : $"Nothing in the catalog matches '{query}' for {field.Label}. Free text works too: /mylore set {field.Id} <text>";
        }
        return found;
    }

    public EditResult Pick(CardFile card, FieldDefinition field, CatalogEntry entry)
    {
        string? race = RaceName(card);
        if (field.Type != FieldType.Catalog || !field.CatalogKinds!.Contains(entry.Kind, StringComparer.OrdinalIgnoreCase)
            || !Allowed(card, field, entry, race))
        {
            return EditResult.Fail($"{entry.Name} does not fit {field.Label} on this card.");
        }
        FieldValue? old = card.Get(field);
        FieldValue value = new() { CatalogId = entry.Id, Visibility = old?.Visibility ?? field.DefaultVisibility };
        EditResult result = Store(card, field, old, value, $"{field.Label} set");
        if (result.Ok && DependentFields.TryGetValue(field.Id, out string[]? dependents) && old?.CatalogId is not null && old.CatalogId != entry.Id)
        {
            // A new race (or homeland) makes the picks that belonged to the old one meaningless.
            List<string> cleared = new();
            foreach (string id in dependents)
            {
                if (CardFields.TryGet(id, out FieldDefinition bound) && card.Get(bound) is { IsEmpty: false } boundValue)
                {
                    card.Put(bound, new FieldValue { Visibility = boundValue.Visibility }, clock());
                    cleared.Add(bound.Label);
                }
            }
            if (cleared.Count > 0)
            {
                string what = field.Id == "race" ? "race" : field.Label.ToLowerInvariant();
                return EditResult.Done(result.Message + $" {string.Join(" and ", cleared)} cleared: {(cleared.Count == 1 ? "it" : "they")} belonged to the old {what}.");
            }
        }
        return result;
    }

    /// <summary>The era rule, model fit, race scope and the rank-belongs-to-affiliation rule.</summary>
    private bool Allowed(CardFile card, FieldDefinition field, CatalogEntry entry, string? race)
    {
        if (entry.IsFuture || (entry.IsHistorical && !field.AllowHistorical))
        {
            return false;
        }
        if (entry.IsImplausible && !allowImplausible)
        {
            return false;
        }
        if (string.Equals(entry.Race, "Mixed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (field.RaceScoped && entry.Race is not null && !string.Equals(entry.Race, race, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (field.Id == "homeRegion")
        {
            // Only places inside the card's homeland province.
            return CardFields.TryGet("homeland", out FieldDefinition homeland)
                && card.Get(homeland)?.CatalogId is string province
                && IsInside(entry, province);
        }
        if (field.Id == "rank" && CardFields.TryGet("affiliation", out FieldDefinition affiliation))
        {
            // A rank belongs to the card's affiliation; titles of their own fit anyone.
            string? faction = card.Get(affiliation)?.CatalogId;
            return (faction is not null && entry.Parent == faction) || (entry.Kind == "title" && entry.Parent is null);
        }
        return true;
    }

    /// <summary>Fields whose picks depend on another field, and are cleared when that one changes.</summary>
    private static readonly Dictionary<string, string[]> DependentFields = new()
    {
        ["race"] = new[] { "subtype", "culture", "upbringing" },
        ["homeland"] = new[] { "homeRegion" },
    };

    /// <summary>The province a race calls home, for "native lands first" in the homeland picker.</summary>
    public static string? NativeProvince(string? race) => (race ?? string.Empty).ToLowerInvariant() switch
    {
        "altmer" => "province.summerset-isles",
        "argonian" => "province.black-marsh",
        "bosmer" => "province.valenwood",
        "breton" => "province.high-rock",
        "dunmer" => "province.morrowind",
        "imperial" => "province.cyrodiil",
        "khajiit" => "province.elsweyr",
        "nord" => "province.skyrim",
        "orsimer" => "province.orsinium",
        "redguard" => "province.hammerfell",
        _ => null,
    };

    /// <summary>True when the entry is the place itself or lies under it (settlement, region, province).</summary>
    public bool IsInside(CatalogEntry entry, string placeId)
    {
        CatalogEntry? at = entry;
        for (int depth = 0; at is not null && depth < 6; depth++)
        {
            if (string.Equals(at.Id, placeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            at = at.Parent is string parent && catalog.TryGet(parent, out CatalogEntry up) ? up : null;
        }
        return false;
    }

    /// <summary>The race name the catalog uses ("Khajiit"), from the card's race pick.</summary>
    public string? RaceName(CardFile card)
    {
        return CardFields.TryGet("race", out FieldDefinition raceField)
            && card.Get(raceField)?.CatalogId is string id
            && catalog.TryGet(id, out CatalogEntry entry)
                ? entry.Race ?? entry.Name
                : null;
    }

    private EditResult Store(CardFile card, FieldDefinition field, FieldValue? old, FieldValue value, string done)
    {
        int length = value.Text?.Length ?? 0;
        if (length > field.MaxLength)
        {
            return EditResult.Fail($"{field.Label} takes at most {field.MaxLength} characters, this is {length}.");
        }
        int total = card.TextLength - (old?.Text?.Length ?? 0) + length;
        if (total > CardFields.MaxCardCharacters)
        {
            return EditResult.Fail($"Your card would have {total} characters, the limit is {CardFields.MaxCardCharacters}.");
        }
        card.Put(field, value, clock());
        string shown = value.CatalogId is not null ? catalog.NameOf(value.CatalogId) : value.Text ?? string.Empty;
        return EditResult.Done($"{done}: {Shorten(shown, 120)} [{ViewerLevel.Word(value.Visibility)}].");
    }

    /// <summary>Removes control characters; single-line fields also lose line breaks. Collapses runs of spaces.</summary>
    internal static string Clean(string text, bool multiline)
    {
        StringBuilder builder = new((text ?? string.Empty).Length);
        bool space = false;
        foreach (char c in text ?? string.Empty)
        {
            bool isBreak = c is '\r' or '\n';
            if (char.IsControl(c) && !(multiline && isBreak))
            {
                if (isBreak || c == '\t')
                {
                    if (!space)
                    {
                        builder.Append(' ');
                        space = true;
                    }
                }
                continue;
            }
            if (c == ' ')
            {
                if (space)
                {
                    continue;
                }
                space = true;
            }
            else
            {
                space = false;
            }
            builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    internal static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";
}
