using System;
using System.Collections.Generic;
using System.Linq;

namespace MESOL.Server.Cards;

/// <summary>The parts of a card. Each part has its own version, which goes up on every save to it.</summary>
public enum CardPart
{
    Identity,
    Heritage,
    Life,
    Look,
    Story,
    Status,

    /// <summary>RP traits (Lazy, Romantic...): a number from 5 to 100, typed once or rolled with 5d20.</summary>
    Traits,
}

public enum FieldType
{
    /// <summary>One line of free text.</summary>
    Text,

    /// <summary>Several sentences; can be written in pieces (<c>/mylore add</c>).</summary>
    LongText,

    /// <summary>A pick from the lore catalog, with free text next to it as the catalog mode allows.</summary>
    Catalog,

    /// <summary>One of a fixed list of words.</summary>
    Choice,

    /// <summary>
    /// An RP trait: <see cref="CardFields.TraitMin"/> to <see cref="CardFields.TraitMax"/>, typed once (and then fixed)
    /// or rolled with 5d20 (one reroll, then fixed). It changes nothing in the game.
    /// </summary>
    Trait,
}

/// <summary>
/// One field of the card. <paramref name="CatalogKinds"/> are the catalog kinds a pick may come from;
/// <paramref name="RaceScoped"/> limits picks to the card's race (plus entries without a race);
/// <paramref name="AllowHistorical"/> lets heritage fields take entries from before 3E 433;
/// <paramref name="Group"/> is the heading a trait is listed under.
/// </summary>
public sealed record FieldDefinition(
    string Id,
    CardPart Part,
    string Label,
    FieldType Type,
    int MaxLength,
    Visibility DefaultVisibility,
    bool VisibilityLocked = false,
    string[]? CatalogKinds = null,
    bool RaceScoped = false,
    bool AllowHistorical = false,
    string[]? Choices = null,
    string? Group = null);

/// <summary>
/// The fields of a card. Lengths follow Oblivion Remastered's 30-character name limit
/// for names. Per-race extra fields come with the page (M3).
/// </summary>
public static class CardFields
{
    private static readonly string[] ConsentChoices = { "no", "ask", "yes" };

    public static readonly IReadOnlyList<FieldDefinition> All = new FieldDefinition[]
    {
        new("firstName", CardPart.Identity, "First name", FieldType.Text, 30, Visibility.Known),
        new("lastName", CardPart.Identity, "Last name", FieldType.Text, 30, Visibility.Known),
        new("title", CardPart.Identity, "Title", FieldType.Text, 30, Visibility.Known),
        new("fullTitle", CardPart.Identity, "Full title", FieldType.Text, 80, Visibility.Known),
        new("seenAs", CardPart.Identity, "Seen as", FieldType.Text, 80, Visibility.Public, VisibilityLocked: true),

        new("race", CardPart.Heritage, "Race", FieldType.Catalog, 30, Visibility.Public, CatalogKinds: new[] { "race" }),
        new("subtype", CardPart.Heritage, "Kind", FieldType.Catalog, 60, Visibility.Public,
            CatalogKinds: new[] { "subtype", "heritage" }, RaceScoped: true, AllowHistorical: true),
        // A province: the race's own first, then "Other Tamrielic Provinces" (PageDesk groups them).
        new("homeland", CardPart.Heritage, "Homeland", FieldType.Catalog, 60, Visibility.Known,
            CatalogKinds: new[] { "province" }),
        // A region or settlement inside the homeland province (CardEditor checks the parents).
        new("homeRegion", CardPart.Heritage, "Home region", FieldType.Catalog, 60, Visibility.Known,
            CatalogKinds: new[] { "region", "settlement" }),
        new("culture", CardPart.Heritage, "Culture", FieldType.Catalog, 60, Visibility.Known,
            CatalogKinds: new[] { "culture", "house", "tribe" }, RaceScoped: true, AllowHistorical: true),
        new("upbringing", CardPart.Heritage, "Upbringing", FieldType.Catalog, 60, Visibility.Known,
            CatalogKinds: new[] { "upbringing" }, RaceScoped: true),
        new("standing", CardPart.Heritage, "Social standing", FieldType.Catalog, 40, Visibility.Known,
            CatalogKinds: new[] { "standing" }),
        new("occupation", CardPart.Heritage, "Former trade", FieldType.Catalog, 40, Visibility.Known,
            CatalogKinds: new[] { "occupation" }),
        new("family", CardPart.Heritage, "Family", FieldType.Text, 200, Visibility.Friends),
        new("ancestry", CardPart.Heritage, "Ancestry", FieldType.Text, 100, Visibility.Friends),

        new("birthsign", CardPart.Life, "Birthsign", FieldType.Catalog, 30, Visibility.Friends, CatalogKinds: new[] { "birthsign" }),
        new("birthday", CardPart.Life, "Birthday", FieldType.Text, 30, Visibility.Friends),
        new("age", CardPart.Life, "Age", FieldType.Text, 30, Visibility.Known),
        new("birthplace", CardPart.Life, "Birthplace", FieldType.Catalog, 60, Visibility.Known,
            CatalogKinds: new[] { "settlement", "region", "province", "homeland" }),
        new("class", CardPart.Life, "Class", FieldType.Catalog, 30, Visibility.Known, CatalogKinds: new[] { "class" }),
        new("faith", CardPart.Life, "Faith", FieldType.Catalog, 60, Visibility.Known, CatalogKinds: new[] { "faith", "deity" }),
        new("affiliation", CardPart.Life, "Affiliation", FieldType.Catalog, 60, Visibility.Known, CatalogKinds: new[] { "faction" }),
        new("rank", CardPart.Life, "Rank", FieldType.Catalog, 40, Visibility.Known, CatalogKinds: new[] { "rank", "title" }),
        new("relationship", CardPart.Life, "Relationship", FieldType.Choice, 20, Visibility.Known,
            Choices: new[] { "unwed", "courting", "betrothed", "wed", "widowed", "estranged", "complicated" }),

        new("height", CardPart.Look, "Height", FieldType.Text, 40, Visibility.Public),
        new("build", CardPart.Look, "Build", FieldType.Text, 40, Visibility.Public),
        new("eyes", CardPart.Look, "Eyes", FieldType.Text, 40, Visibility.Public),
        new("hair", CardPart.Look, "Hair", FieldType.Text, 40, Visibility.Public),
        new("marks", CardPart.Look, "Marks", FieldType.Text, 80, Visibility.Public),
        new("glance", CardPart.Look, "At first glance", FieldType.Text, 300, Visibility.Public),

        new("physical", CardPart.Story, "Appearance", FieldType.LongText, 2000, Visibility.Friends),
        new("personality", CardPart.Story, "Personality", FieldType.LongText, 2000, Visibility.Friends),
        new("history", CardPart.Story, "History", FieldType.LongText, 2000, Visibility.Friends),
        new("notes", CardPart.Story, "Notes", FieldType.LongText, 2000, Visibility.Private, VisibilityLocked: true),

        new("icOoc", CardPart.Status, "Status", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true,
            Choices: new[] { "ic", "ooc", "lfrp" }),
        new("currently", CardPart.Status, "Currently", FieldType.Text, 200, Visibility.Public),
        new("oocInfo", CardPart.Status, "OOC", FieldType.Text, 300, Visibility.Public),
        new("consent.injury", CardPart.Status, "Injury", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true, Choices: ConsentChoices),
        new("consent.death", CardPart.Status, "Death", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true, Choices: ConsentChoices),
        new("consent.romance", CardPart.Status, "Romance", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true, Choices: ConsentChoices),
        new("consent.crime", CardPart.Status, "Crime", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true, Choices: ConsentChoices),
        new("consent.control", CardPart.Status, "Loss of control", FieldType.Choice, 10, Visibility.Public, VisibilityLocked: true, Choices: ConsentChoices),
        new("experience", CardPart.Status, "RP experience", FieldType.Choice, 10, Visibility.Public,
            Choices: new[] { "newcomer", "casual", "veteran", "guide" }),

        // RP traits in the spirit of tabletop personality traits: how strongly each one drives the character.
        // They change nothing in the game: no skills, no attributes, no looks.
        Trait("fearful", "Fearful", "Temperament"),
        Trait("hotTempered", "Hot-tempered", "Temperament"),
        Trait("melancholic", "Melancholic", "Temperament"),
        Trait("reckless", "Reckless", "Temperament"),
        Trait("stubborn", "Stubborn", "Temperament"),
        Trait("patient", "Patient", "Temperament"),
        Trait("romantic", "Romantic", "Heart"),
        Trait("jealous", "Jealous", "Heart"),
        Trait("loyal", "Loyal", "Heart"),
        Trait("merciful", "Merciful", "Heart"),
        Trait("vengeful", "Vengeful", "Heart"),
        Trait("lustful", "Lustful", "Heart"),
        Trait("lazy", "Lazy", "Vices and habits"),
        Trait("slovenly", "Slovenly", "Vices and habits"),
        Trait("gluttonous", "Gluttonous", "Vices and habits"),
        Trait("greedy", "Greedy", "Vices and habits"),
        Trait("fondOfDrink", "Fond of drink", "Vices and habits"),
        Trait("superstitious", "Superstitious", "Vices and habits"),
        Trait("proud", "Proud", "Mind and manners"),
        Trait("deceitful", "Deceitful", "Mind and manners"),
        Trait("suspicious", "Suspicious", "Mind and manners"),
        Trait("curious", "Curious", "Mind and manners"),
        Trait("pious", "Pious", "Mind and manners"),
        Trait("honourable", "Honourable", "Mind and manners"),
    };

    /// <summary>The lowest and highest trait value: what 5d20 can give.</summary>
    public const int TraitMin = 5;

    public const int TraitMax = 100;

    /// <summary>How many d20 a trait roll throws.</summary>
    public const int TraitDice = 5;

    private static FieldDefinition Trait(string id, string label, string group) =>
        new("trait." + id, CardPart.Traits, label, FieldType.Trait, 3, Visibility.Friends, Group: group);

    /// <summary>A word for how strongly a trait drives the character.</summary>
    public static string TraitWord(int value) => value switch
    {
        <= 20 => "faintly",
        <= 40 => "somewhat",
        <= 60 => "notably",
        <= 80 => "strongly",
        _ => "overwhelmingly",
    };

    private static readonly Dictionary<string, FieldDefinition> ById =
        All.ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string id, out FieldDefinition field)
    {
        return ById.TryGetValue((id ?? string.Empty).Trim(), out field!);
    }

    /// <summary>Hard cap on the text of a whole card, in characters.</summary>
    public const int MaxCardCharacters = 16_000;

    /// <summary>One line under each field on the card page: what goes there.</summary>
    public static string HintOf(string id) => Hints.TryGetValue(id, out string? hint) ? hint : string.Empty;

    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firstName"] = "Your character's given name. Others see it once you introduce yourself.",
        ["lastName"] = "Family or clan name, if your character has one.",
        ["title"] = "A short title shown with the name, like \"Caravan guard\" or \"of Leyawiin\".",
        ["fullTitle"] = "The long form, for those who like ceremony.",
        ["seenAs"] = "What strangers see before they know your name, like \"a scarred Khajiit with a merchant's sash\". Always public.",
        ["race"] = "Picked from the lore list. Your character in the game is the best guide.",
        ["subtype"] = "Your kind within the race: a Khajiit furstock, a Bosmer clan, a Nord's descent. Pick the race first.",
        ["homeland"] = "The province your character comes from: your race's own lands first, then the other provinces of Tamriel.",
        ["homeRegion"] = "A region or town inside your homeland. Pick the homeland first.",
        ["culture"] = "A house, tribe or culture of your race.",
        ["upbringing"] = "How your character was raised. Your race's own ways come first.",
        ["standing"] = "Where your character stood in society before setting out.",
        ["family"] = "Parents, siblings, a spouse or children, in your own words.",
        ["occupation"] = "What your character did for a living before adventuring.",
        ["trait.fearful"] = "Shies from danger, flinches at threats, looks for the way out.",
        ["trait.hotTempered"] = "Quick to anger and slow to cool. Insults are answered at once.",
        ["trait.melancholic"] = "Prone to gloom, brooding and long silences.",
        ["trait.reckless"] = "Acts first and thinks later. Danger is a dare.",
        ["trait.stubborn"] = "Once a mind is made up, it stays made up.",
        ["trait.patient"] = "Waits, listens and lets things ripen.",
        ["trait.romantic"] = "Falls for people, poems and grand gestures. A romantic soul.",
        ["trait.jealous"] = "Guards what is theirs, people included, and resents rivals.",
        ["trait.loyal"] = "Stands by friends, lords and oaths, even when it costs.",
        ["trait.merciful"] = "Spares the beaten and forgives the sorry.",
        ["trait.vengeful"] = "Never forgets a wrong, and pays it back with interest.",
        ["trait.lustful"] = "Led by desire more than by sense.",
        ["trait.lazy"] = "Avoids effort, sleeps late, lets others carry the load.",
        ["trait.slovenly"] = "Unwashed, untidy, careless with gear and looks.",
        ["trait.gluttonous"] = "Eats too much, too often, and talks about food.",
        ["trait.greedy"] = "Wants more coin and more things, and rarely shares.",
        ["trait.fondOfDrink"] = "Mead, wine or skooma: never far from a cup.",
        ["trait.superstitious"] = "Reads omens, carries charms, fears curses and the wrong moon.",
        ["trait.proud"] = "Thinks highly of self and takes slights badly.",
        ["trait.deceitful"] = "Lies easily, for gain or for fun.",
        ["trait.suspicious"] = "Trusts nobody and sees plots everywhere.",
        ["trait.curious"] = "Must know, must open, must see. Questions everything.",
        ["trait.pious"] = "Prays, keeps the rites and lives by the faith.",
        ["trait.honourable"] = "Keeps their word and fights fair, even with enemies.",
        ["ancestry"] = "Family history in your own words: a grandmother from Skyrim, a line of smiths.",
        ["birthsign"] = "The constellation your character was born under.",
        ["birthday"] = "In your own words, like \"16th of Rain's Hand\".",
        ["age"] = "A number or a description, like \"late thirties\".",
        ["birthplace"] = "A city, region or province.",
        ["class"] = "What your character does best. Picked from Oblivion's classes.",
        ["faith"] = "A god, a pantheon or a Daedric Prince.",
        ["affiliation"] = "A guild, house or order your character belongs to in 3E 433.",
        ["rank"] = "A rank in that affiliation, or a title of its own. Pick the affiliation first.",
        ["relationship"] = "Unwed, courting, wed and the rest.",
        ["height"] = "Free text, like \"tall for a Bosmer\".",
        ["build"] = "Free text, like \"wiry\" or \"broad-shouldered\".",
        ["eyes"] = "Free text.",
        ["hair"] = "Free text: hair, fur or scales.",
        ["marks"] = "Scars, tattoos, war paint, anything others would notice.",
        ["glance"] = "What someone notices in the first few seconds.",
        ["physical"] = "A longer description of how your character looks.",
        ["personality"] = "How your character thinks, talks and behaves.",
        ["history"] = "Your character's story so far.",
        ["notes"] = "Only for you: reminders, plans, debts. Nobody else ever sees this.",
        ["icOoc"] = "In character, out of character, or looking for roleplay. Always public.",
        ["currently"] = "What your character is doing right now, for anyone passing by.",
        ["oocInfo"] = "Out of character notes: time zone, languages, what you enjoy in RP.",
        ["consent.injury"] = "May others injure your character in a scene? Always public.",
        ["consent.death"] = "May your character die in a scene? Always public.",
        ["consent.romance"] = "Are you open to romance in RP? Always public.",
        ["consent.crime"] = "May others steal from or cheat your character? Always public.",
        ["consent.control"] = "Mind control, charm and the like. Always public.",
        ["experience"] = "How much roleplay you have done, so others know what to expect.",
    };
}
