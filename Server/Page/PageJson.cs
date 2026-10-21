using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;

namespace MESOL.Server.Page;

/// <summary>
/// What the card page receives, as JSON. A card for another viewer is built only from what <see cref="CardView"/>
/// lets through, exactly like the chat version, so the page never holds a field its viewer may not see.
/// </summary>
public static class PageJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Shown in the header line, so not repeated in the list (as in chat).
    private static readonly HashSet<string> InHeader = new(StringComparer.OrdinalIgnoreCase) { "firstName", "lastName", "title", "seenAs", "icOoc" };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public static string Word(Visibility visibility) => ViewerLevel.Word(visibility).ToLowerInvariant();

    public static string PartName(CardPart part) => CardFile.PartKey(part);

    public static string TypeName(FieldType type) => type switch
    {
        FieldType.Text => "text",
        FieldType.LongText => "longText",
        FieldType.Catalog => "catalog",
        FieldType.Choice => "choice",
        FieldType.Trait => "trait",
        _ => "text",
    };

    /// <summary>Every field the editor shows, with its rules: the page builds its tabs and inputs from this.</summary>
    public static object Fields() => CardFields.All.Select(field => new
    {
        id = field.Id,
        part = PartName(field.Part),
        label = field.Label,
        type = TypeName(field.Type),
        max = field.MaxLength,
        vis = Word(field.DefaultVisibility),
        locked = field.VisibilityLocked,
        hint = CardFields.HintOf(field.Id),
        choices = field.Choices?.Select(c => new { id = c, name = Capital(CardText.ChoiceWord(c)) }).ToArray(),
        needs = field.Id switch
        {
            "subtype" or "culture" => "race",
            "rank" => "affiliation",
            "homeRegion" => "homeland",
            _ => null,
        },
        group = field.Group,
        // Only for the browser preview's stand-in server (web-src/src/preview.js); the page itself asks the server.
        kinds = field.CatalogKinds,
        raceScoped = field.RaceScoped,
        historical = field.AllowHistorical,
    }).ToArray();

    /// <summary>The owner's own values, every field (empty ones too, for their visibility).</summary>
    public static Dictionary<string, object> Values(CardFile card, CardText text, LoreCatalog catalog)
    {
        Dictionary<string, object> values = new();
        foreach (FieldDefinition field in CardFields.All)
        {
            FieldValue? value = card.Get(field);
            bool empty = value is null || value.IsEmpty;
            values[field.Id] = new
            {
                catalogId = value?.CatalogId,
                text = value?.Text,
                shown = empty ? string.Empty : text.Display(field, value!),
                summary = catalog.TryGet(value?.CatalogId, out CatalogEntry entry) ? entry.Summary : null,
                number = value?.Number,
                @fixed = value?.Fixed ?? false,
                rolls = value?.Rolls?.Select(r => new { dice = r.Dice, total = r.Total }).ToArray(),
                vis = Word(value is null ? field.DefaultVisibility : CardView.EffectiveVisibility(field, value)),
                empty,
            };
        }
        return values;
    }

    /// <summary>A card as a viewer at <paramref name="level"/> sees it: the header line and the visible fields in card order.</summary>
    public static object View(CardFile card, Visibility level, CardText text)
    {
        return new
        {
            header = text.Header(card, level),
            level = Word(level),
            fields = CardView.Visible(card, level)
                .Where(item => !InHeader.Contains(item.Field.Id))
                .Select(item => new
                {
                    id = item.Field.Id,
                    part = PartName(item.Field.Part),
                    label = item.Field.Label,
                    text = text.Display(item.Field, item.Value),
                    @long = item.Field.Type == FieldType.LongText,
                })
                .ToArray(),
        };
    }

    public static object Option(CatalogEntry entry, string? group = null) => new
    {
        group,
        id = entry.Id,
        name = entry.Name,
        summary = entry.Summary,
        kind = entry.Kind,
        race = entry.Race,
        origin = entry.RemasteredOrigin,
        historical = entry.IsHistorical,
    };

    /// <summary>The server's own message: open the editor (/mylore).</summary>
    public static string EditPush() => Serialize(new { op = "edit" });

    /// <summary>The server's own message: show another player's card (/lore), already cut to what this viewer may see.</summary>
    public static string ViewPush(string who, CardFile? card, Visibility level, CardText text) => Serialize(new
    {
        op = "view",
        who,
        view = card is null || card.IsEmpty ? null : View(card, level, text),
    });

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
