using System;
using System.Collections.Generic;
using System.Linq;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Players;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Commands;

/// <summary>
/// /mylore: opens the card page (End does the same on the client), or, without the page or with /mylore card,
/// shows and edits the player's own card through chat. The page's requests (<see cref="Page.PageDesk"/>) and these
/// commands both end in <see cref="CardEditor"/>, so the rules are the same.
/// </summary>
[Service]
public sealed partial class MyLoreCommand(
    IChat chat, IPlayers players, CardStore cards, CatalogService catalogs, GameRace race, Page.PageLink page, MesolConfig config, ILogger logger)
{
    internal const string Usage = "/mylore [card|set|add|pick|vis|clear|show|preview|fields|help] - your lore card (End). /mylore help explains each";

    private static readonly string[] HelpLines =
    {
        "/mylore or End - your card on its own page. /mylore card - your card here in chat, with who sees each field",
        "/mylore set <field> <text> - fill a field, e.g. /mylore set firstName Ri'Zahra",
        "/mylore add <field> <text> - add to a long field (physical, personality, history, notes)",
        "/mylore pick <field> <search> - pick from the lore catalog, e.g. /mylore pick subtype cathay, then /mylore pick subtype 2",
        "/mylore vis <field> public|known|friends|private - who sees a field",
        "/mylore clear <field> - empty a field. /mylore show <field> - one field in full",
        "/mylore preview public|known|friends - your card as others see it. /mylore fields - every field",
    };

    // The last catalog offer per player, so "/mylore pick subtype 2" means the second entry of that list.
    private readonly Dictionary<PlayerId, (string Field, List<string> Ids)> _offers = new();

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The game's race for a player (catalog id); replaced in the tests.</summary>
    internal Func<PlayerId, string?> RaceOf { get; set; } = race.RaceIdOf;

    private void Start()
    {
        chat.AddCommand("mylore", Usage, Run);
        logger.LogInformation("MESOL: /mylore registered, catalog mode {Mode}.", CardEditor.ParseMode(config.CatalogMode));
    }

    /// <summary>The /mylore handler. Never throws: a failure is logged and the player gets a short message.</summary>
    internal void Run(IChat chat, PlayerId sender, string arguments)
    {
        try
        {
            Handle(chat, sender, arguments ?? string.Empty);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: '/mylore {Arguments}' from {Sender} failed.", arguments, sender);
            Send(chat, sender, "That didn't work. Try again.", ChatLineKind.Rejection);
        }
    }

    private void Handle(IChat chat, PlayerId sender, string arguments)
    {
        (string verb, string rest) = Split(arguments);
        verb = verb.ToLowerInvariant();
        if (verb == "help")
        {
            foreach (string line in HelpLines)
            {
                Send(chat, sender, line, ChatLineKind.CommandReply);
            }
            return;
        }
        if (verb == "fields")
        {
            foreach (CardPart part in Enum.GetValues<CardPart>())
            {
                string ids = string.Join(", ", CardFields.All.Where(f => f.Part == part).Select(f => f.Type == FieldType.Catalog ? f.Id + "*" : f.Id));
                Send(chat, sender, $"{part}: {ids}", ChatLineKind.CommandReply);
            }
            Send(chat, sender, "* = picked from the lore catalog with /mylore pick", ChatLineKind.CommandReply);
            return;
        }
        if (players.GetReadyMId(sender) is not Guid owner)
        {
            Send(chat, sender, "The server doesn't know your ReadyM account yet, so your card can't be loaded. Try again in a moment.", ChatLineKind.Rejection);
            return;
        }

        CardFile card = cards.GetOrCreate(owner);
        LoreCatalog catalog = catalogs.Current;
        CatalogMode mode = CardEditor.ParseMode(config.CatalogMode);
        CardEditor editor = new(catalog, mode, config.AllowImplausibleSubtypes, Clock);
        CardText text = new(catalog, markCustom: mode == CatalogMode.CanonPlus);
        if (cards.CanSave(owner) && editor.DropCustom(card))
        {
            cards.Save(card);
        }

        if (verb.Length == 0 && page.HasPage(sender))
        {
            page.Push(sender, Page.PageJson.EditPush());
            return;
        }
        if (verb is "" or "card")
        {
            ShowOwn(chat, sender, card, catalog, text);
            return;
        }
        if (verb == "preview")
        {
            Visibility level = Visibility.Public;
            if (rest.Length > 0 && (!ViewerLevel.TryParse(rest, out level) || level == Visibility.Private))
            {
                Send(chat, sender, "Preview as public, known or friends.", ChatLineKind.Rejection);
                return;
            }
            Send(chat, sender, $"How a {ViewerLevel.Word(level)} viewer sees you:", ChatLineKind.CommandReply);
            foreach (string line in text.Lines(card, level, showVisibility: false, moreHint: string.Empty))
            {
                Send(chat, sender, line, ChatLineKind.CommandReply);
            }
            return;
        }
        if (verb is not ("set" or "add" or "pick" or "vis" or "clear" or "show"))
        {
            Send(chat, sender, $"There is no /mylore {verb}. /mylore help lists them.", ChatLineKind.Rejection);
            return;
        }

        (string fieldId, string value) = Split(rest);
        if (!CardFields.TryGet(fieldId, out FieldDefinition field))
        {
            Send(chat, sender, fieldId.Length == 0
                ? $"Which field? /mylore fields lists them."
                : $"There is no field '{fieldId}'. /mylore fields lists them.", ChatLineKind.Rejection);
            return;
        }
        if (verb == "show")
        {
            if (card.Get(field) is { IsEmpty: false } shown)
            {
                foreach (string line in text.Full(field, shown))
                {
                    Send(chat, sender, line, ChatLineKind.CommandReply);
                }
            }
            else
            {
                Send(chat, sender, $"{field.Label} is empty.", ChatLineKind.CommandReply);
            }
            return;
        }
        if (!cards.CanSave(owner))
        {
            Send(chat, sender, "Your card file could not be read, so it can't be changed right now. The server log says why.", ChatLineKind.Rejection);
            return;
        }

        EditResult? result = verb switch
        {
            "set" => editor.Set(card, field, value),
            "add" => editor.Append(card, field, value),
            "clear" => editor.Clear(card, field),
            "vis" => ViewerLevel.TryParse(value, out Visibility visibility)
                ? editor.SetVisibility(card, field, visibility)
                : EditResult.Fail("Who should see it: public, known, friends or private?"),
            _ => Pick(chat, sender, card, catalog, editor, field, value),
        };
        if (result is null)
        {
            return;
        }
        if (result.Ok)
        {
            cards.Save(card);
            logger.LogInformation("MESOL: {Owner} /mylore {Verb} {Field}.", owner, verb, field.Id);
        }
        Send(chat, sender, result.Message, result.Ok ? ChatLineKind.CommandReply : ChatLineKind.Rejection);
    }

    /// <summary>
    /// "pick field 2" takes the second entry of the last offer for that field; "pick field text" searches and
    /// either picks the only match or shows a numbered offer (and returns null).
    /// </summary>
    private EditResult? Pick(IChat chat, PlayerId sender, CardFile card, LoreCatalog catalog, CardEditor editor, FieldDefinition field, string query)
    {
        if (int.TryParse(query, out int number) && _offers.TryGetValue(sender, out var offer) && offer.Field == field.Id)
        {
            if (number < 1 || number > offer.Ids.Count || !catalog.TryGet(offer.Ids[number - 1], out CatalogEntry chosen))
            {
                return EditResult.Fail($"Pick a number from 1 to {offer.Ids.Count}.");
            }
            return PickEntry(sender, card, editor, field, chosen);
        }
        if (catalogs.Problem is string problem)
        {
            return EditResult.Fail(problem);
        }
        List<CatalogEntry> found = editor.Candidates(card, field, query, out string? why);
        if (why is not null)
        {
            return EditResult.Fail(why);
        }
        if (found.Count == 1)
        {
            return PickEntry(sender, card, editor, field, found[0]);
        }
        _offers[sender] = (field.Id, found.Select(e => e.Id).ToList());
        string list = string.Join(", ", found.Select((e, i) => $"{i + 1} {e.Name}" + (e.RemasteredOrigin ? " (Origin)" : string.Empty)));
        foreach (string line in CardText.Chunks($"{field.Label}: {list}. Then: /mylore pick {field.Id} <number>"))
        {
            Send(chat, sender, line, ChatLineKind.CommandReply);
        }
        if (found.Count == CardEditor.MaxPicks)
        {
            Send(chat, sender, "There are more. Type more of the name to narrow it down.", ChatLineKind.CommandReply);
        }
        return null;
    }

    private EditResult PickEntry(PlayerId sender, CardFile card, CardEditor editor, FieldDefinition field, CatalogEntry entry)
    {
        if (field.Id == "race" && config.RaceLock && RaceOf(sender) is string gameRace && gameRace != entry.Id)
        {
            return EditResult.Fail($"Your character is a {catalogs.Current.NameOf(gameRace)} in the game, and this server keeps the card's race the same.");
        }
        _offers.Remove(sender);
        return editor.Pick(card, field, entry);
    }

    private void ShowOwn(IChat chat, PlayerId sender, CardFile card, LoreCatalog catalog, CardText text)
    {
        if (card.IsEmpty)
        {
            Send(chat, sender, "Your card is empty. Start with /mylore set firstName <name>, or see /mylore help.", ChatLineKind.CommandReply);
        }
        else
        {
            foreach (string line in text.Lines(card, Visibility.Private, showVisibility: true, moreHint: "Long fields are cut here: /mylore show <field> prints one in full."))
            {
                Send(chat, sender, line, ChatLineKind.CommandReply);
            }
        }
        if (CardFields.TryGet("race", out FieldDefinition raceField) && card.Get(raceField) is not { CatalogId: not null }
            && SafeRaceOf(sender) is string gameRace)
        {
            string name = catalog.NameOf(gameRace);
            Send(chat, sender, $"Your character looks like a {name} in the game: /mylore pick race {name}", ChatLineKind.CommandReply);
        }
    }

    /// <summary>The race hint is a nicety: if reading the game's race fails, the card still shows.</summary>
    private string? SafeRaceOf(PlayerId player)
    {
        try
        {
            return RaceOf(player);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "MESOL: could not read the game race of {Player}.", player);
            return null;
        }
    }

    private static (string First, string Remainder) Split(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        int space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    private void Send(IChat chat, PlayerId player, string text, ChatLineKind kind)
    {
        try
        {
            chat.SendTo(player, text, kind);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: could not send a line to {Player}.", player);
        }
    }
}
