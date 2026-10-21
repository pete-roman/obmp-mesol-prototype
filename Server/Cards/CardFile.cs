using System;
using System.Collections.Generic;
using System.Linq;

namespace MESOL.Server.Cards;

/// <summary>
/// One player's card as it is saved: <c>cards/&lt;ReadyM id&gt;.json</c>. Server only, never sent as a whole;
/// viewers get only what <see cref="CardView"/> lets through.
/// </summary>
public sealed class CardFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The owner's ReadyM account id.</summary>
    public Guid Owner { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset Updated { get; set; }

    /// <summary>By part name (<see cref="CardPart"/>, lower camel case in the file).</summary>
    public Dictionary<string, PartData> Parts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static CardFile New(Guid owner, DateTimeOffset now) => new() { Owner = owner, Created = now, Updated = now };

    public FieldValue? Get(FieldDefinition field)
    {
        return Parts.TryGetValue(PartKey(field.Part), out PartData? part) && part.Fields.TryGetValue(field.Id, out FieldValue? value)
            ? value
            : null;
    }

    /// <summary>Sets or (with null) removes a field and bumps its part's version.</summary>
    public void Put(FieldDefinition field, FieldValue? value, DateTimeOffset now)
    {
        string key = PartKey(field.Part);
        if (!Parts.TryGetValue(key, out PartData? part))
        {
            part = new PartData();
            Parts[key] = part;
        }
        if (value is null)
        {
            part.Fields.Remove(field.Id);
        }
        else
        {
            part.Fields[field.Id] = value;
        }
        part.Version++;
        Updated = now;
    }

    public long VersionOf(CardPart part) => Parts.TryGetValue(PartKey(part), out PartData? data) ? data.Version : 0;

    /// <summary>True when no field has content (a field may hold only its visibility choice).</summary>
    public bool IsEmpty => Parts.Values.All(part => part.Fields.Values.All(value => value.IsEmpty));

    /// <summary>All text on the card, for the size cap.</summary>
    public int TextLength => Parts.Values.SelectMany(part => part.Fields.Values).Sum(value => value.Text?.Length ?? 0);

    public static string PartKey(CardPart part)
    {
        string name = part.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public sealed class PartData
{
    /// <summary>Goes up on every save to this part. A viewer only needs a part again when it changed.</summary>
    public long Version { get; set; }

    public Dictionary<string, FieldValue> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FieldValue
{
    /// <summary>A catalog entry id, for example <c>khajiit.furstock.cathay-raht</c>.</summary>
    public string? CatalogId { get; set; }

    /// <summary>Free text: the whole value, or an addition next to a catalog pick ("Rimmen outskirts").</summary>
    public string? Text { get; set; }

    /// <summary>True when free text stands in for a catalog pick; shown as "custom" on canonPlus servers.</summary>
    public bool NonCanon { get; set; }

    public Visibility Visibility { get; set; }

    /// <summary>A trait's value, 5 to 100.</summary>
    public int? Number { get; set; }

    /// <summary>A trait that can no longer change: typed in, kept after a roll, or rerolled.</summary>
    public bool Fixed { get; set; }

    /// <summary>A trait's rolls, oldest first: one, or two after the reroll. Empty when it was typed in.</summary>
    public List<TraitRoll>? Rolls { get; set; }

    public bool IsEmpty => string.IsNullOrEmpty(CatalogId) && string.IsNullOrEmpty(Text) && Number is null;

    /// <summary>The same value with another visibility.</summary>
    public FieldValue WithVisibility(Visibility visibility) => new()
    {
        CatalogId = CatalogId,
        Text = Text,
        NonCanon = NonCanon,
        Number = Number,
        Fixed = Fixed,
        Rolls = Rolls,
        Visibility = visibility,
    };
}

/// <summary>One trait roll as RpRoll made it: each d20 and when.</summary>
public sealed class TraitRoll
{
    public int[] Dice { get; set; } = Array.Empty<int>();

    public int Total { get; set; }

    public DateTimeOffset At { get; set; }
}
