using System;
using System.Collections.Generic;
using System.Linq;
using MESOL.Server.Catalog;

namespace MESOL.Server.Cards;

/// <summary>
/// A card as chat lines, as one viewer may see it. Every line stays under chat's 240-character limit.
/// Only fields that pass <see cref="CardView"/> are ever read here.
/// </summary>
public sealed class CardText(LoreCatalog catalog, bool markCustom)
{
    public const int LineLimit = 230;
    public const int LongTextPreview = 140;

    private static readonly HashSet<string> InHeader = new(StringComparer.OrdinalIgnoreCase) { "firstName", "lastName", "title", "seenAs" };

    /// <summary>
    /// The first line: name and title when the viewer may see them, otherwise how others see the character
    /// ("seen as"), otherwise the race, otherwise "someone you don't know".
    /// </summary>
    public string Header(CardFile card, Visibility level)
    {
        string name = string.Join(" ", new[] { "firstName", "lastName" }.Select(id => Value(card, id, level)).Where(v => v.Length > 0));
        string title = Value(card, "title", level);
        string status = StatusTag(card);
        if (name.Length > 0)
        {
            return Fit((title.Length > 0 ? $"{name}, {title}" : name) + status);
        }
        string seenAs = Value(card, "seenAs", level);
        if (seenAs.Length > 0)
        {
            return Fit(seenAs + status);
        }
        string race = Value(card, "race", level);
        return Fit((race.Length > 0 ? $"a {race}" : "someone you don't know") + status);
    }

    /// <summary>
    /// The card in chat: the header, then "Label: value" items joined into lines. Long texts are cut to a preview;
    /// <paramref name="moreHint"/> says how to read one in full. With <paramref name="showVisibility"/> every item
    /// gets its tier ("[Known]"), for the owner's own view.
    /// </summary>
    public List<string> Lines(CardFile card, Visibility level, bool showVisibility, string moreHint)
    {
        List<string> lines = new() { Header(card, level) };
        List<string> items = new();
        bool cut = false;
        foreach ((FieldDefinition field, FieldValue value) in CardView.Visible(card, level))
        {
            if (field.Id == "icOoc" || (!showVisibility && InHeader.Contains(field.Id)))
            {
                continue;
            }
            string shown = Display(field, value);
            if (field.Type == FieldType.LongText && shown.Length > LongTextPreview)
            {
                shown = CardEditor.Shorten(shown, LongTextPreview);
                cut = true;
            }
            string tag = showVisibility ? $" [{ViewerLevel.Word(CardView.EffectiveVisibility(field, value))}]" : string.Empty;
            items.Add($"{field.Label}: {shown}{tag}");
        }
        lines.AddRange(Wrap(items, " | "));
        if (cut && moreHint.Length > 0)
        {
            lines.Add(moreHint);
        }
        return lines;
    }

    /// <summary>One field in full, in as many lines as it needs.</summary>
    public List<string> Full(FieldDefinition field, FieldValue value)
    {
        return Chunks($"{field.Label}: {Display(field, value)}");
    }

    public string Display(FieldDefinition field, FieldValue value)
    {
        if (field.Type == FieldType.Trait && value.Number is int number)
        {
            string how = value.Rolls is { Count: > 0 } ? "rolled" : "chosen";
            return $"{number} ({CardFields.TraitWord(number)}, {how})";
        }
        string text = value.Text ?? string.Empty;
        if (field.Type == FieldType.Choice)
        {
            text = ChoiceWord(text);
        }
        string shown = value.CatalogId is null
            ? text
            : text.Length > 0 ? $"{catalog.NameOf(value.CatalogId)} ({text})" : catalog.NameOf(value.CatalogId);
        return value.NonCanon && markCustom ? shown + " (custom)" : shown;
    }

    private string Value(CardFile card, string id, Visibility level)
    {
        return CardFields.TryGet(id, out FieldDefinition field) && CardView.CanSee(card, field, level)
            ? Display(field, card.Get(field)!)
            : string.Empty;
    }

    /// <summary>IC/OOC is always public, so it rides on the header.</summary>
    private static string StatusTag(CardFile card)
    {
        return CardFields.TryGet("icOoc", out FieldDefinition field) && card.Get(field)?.Text is string status
            ? $" ({ChoiceWord(status)})"
            : string.Empty;
    }

    internal static string ChoiceWord(string choice) => choice switch
    {
        "ic" => "in character",
        "ooc" => "out of character",
        "lfrp" => "looking for RP",
        "ask" => "ask first",
        "wed" => "wed under Mara",
        _ => choice,
    };

    private static string Fit(string line) => CardEditor.Shorten(line, LineLimit);

    private static IEnumerable<string> Wrap(IEnumerable<string> items, string separator)
    {
        string line = string.Empty;
        foreach (string item in items)
        {
            if (item.Length > LineLimit)
            {
                if (line.Length > 0)
                {
                    yield return line;
                    line = string.Empty;
                }
                foreach (string chunk in Chunks(item))
                {
                    yield return chunk;
                }
                continue;
            }
            if (line.Length > 0 && line.Length + separator.Length + item.Length > LineLimit)
            {
                yield return line;
                line = string.Empty;
            }
            line = line.Length == 0 ? item : line + separator + item;
        }
        if (line.Length > 0)
        {
            yield return line;
        }
    }

    /// <summary>Splits at spaces into lines of at most <see cref="LineLimit"/> characters.</summary>
    internal static List<string> Chunks(string text)
    {
        List<string> chunks = new();
        string rest = text.Trim();
        while (rest.Length > LineLimit)
        {
            int cut = rest.LastIndexOf(' ', LineLimit);
            if (cut <= 0)
            {
                cut = LineLimit;
            }
            chunks.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0)
        {
            chunks.Add(rest);
        }
        return chunks;
    }
}
