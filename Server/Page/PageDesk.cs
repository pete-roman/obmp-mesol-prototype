using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Players;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Page;

/// <summary>
/// The card page's requests, one JSON text in, one out. Every change goes through <see cref="CardEditor"/>, the same
/// rules as /mylore; the page only ever edits its own player's card, found by the sender's ReadyM id.
///
/// Requests (<c>op</c>): <c>load</c>; <c>options</c> (field); <c>set</c> (field, text), <c>pick</c> (field, id),
/// <c>vis</c> (field, vis), <c>clear</c> (field); <c>roll</c> and <c>keep</c> (a trait: 5d20 through RpRoll, one reroll,
/// or keep the first roll); <c>preview</c> (level). Every answer has <c>ok</c> and, when something went wrong or
/// changed, a <c>message</c> to show as it is. A trait is typed with <c>set</c> and its number as text.
/// </summary>
[Service]
public sealed partial class PageDesk(
    IPlayers players, CardStore cards, CatalogService catalogs, GameRace race, TraitDice dice, Relations.Social social,
    MesolConfig config, ILogger logger)
{
    /// <summary>The most catalog entries one picker gets (the biggest list, places, is under 200).</summary>
    public const int MaxOptions = 400;

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The game's race for a player (catalog id); replaced in the tests.</summary>
    internal Func<PlayerId, string?> RaceOf { get; set; } = race.RaceIdOf;

    /// <summary>Rolls 5d20 for a trait (RpRoll); replaced in the tests.</summary>
    internal Func<PlayerId, string, int[]> RollDice { get; set; } = (player, label) => dice.Roll(player, label);

    public const string NativeLands = "Native lands";

    public const string OtherProvinces = "Other Tamrielic Provinces";

    /// <summary>Answers one request. Never throws: a failure is logged and answered with a short message.</summary>
    public string Handle(PlayerId sender, string requestJson)
    {
        string op = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestJson);
            JsonElement request = document.RootElement;
            op = Text(request, "op");
            return Serialize(Run(sender, op, request));
        }
        catch (JsonException)
        {
            return Fail("That request didn't make sense.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: page request '{Op}' from {Sender} failed.", op, sender);
            return Fail("That didn't work. Try again.");
        }
    }

    private object Run(PlayerId sender, string op, JsonElement request)
    {
        if (SocialOps.Contains(op))
        {
            return RunSocial(sender, op, request);
        }
        if (players.GetReadyMId(sender) is not Guid owner)
        {
            return Failed("The server doesn't know your ReadyM account yet, so your card can't be loaded. Try again in a moment.");
        }
        CardFile card = cards.GetOrCreate(owner);
        LoreCatalog catalog = catalogs.Current;
        CatalogMode mode = CardEditor.ParseMode(config.CatalogMode);
        CardEditor editor = new(catalog, mode, config.AllowImplausibleSubtypes, Clock);
        CardText text = new(catalog, markCustom: mode == CatalogMode.CanonPlus);
        if (cards.CanSave(owner) && editor.DropCustom(card))
        {
            cards.Save(card);
            logger.LogInformation("MESOL: {Owner}'s card lost typed text in catalog fields (the server is canon).", owner);
        }

        if (op == "load")
        {
            return new
            {
                ok = true,
                mode = mode.ToString().ToLowerInvariant(),
                readOnly = !cards.CanSave(owner),
                fields = PageJson.Fields(),
                card = PageJson.Values(card, text, catalog),
                raceHint = RaceHint(sender, card, catalog),
                maxCard = CardFields.MaxCardCharacters,
                used = card.TextLength,
            };
        }
        if (op == "preview")
        {
            string word = Text(request, "level");
            if (!ViewerLevel.TryParse(word, out Visibility level) || level == Visibility.Private)
            {
                return Failed("Preview as public, known or friends.");
            }
            return new { ok = true, view = PageJson.View(card, level, text) };
        }

        string fieldId = Text(request, "field");
        if (!CardFields.TryGet(fieldId, out FieldDefinition field))
        {
            return Failed($"There is no field '{fieldId}'.");
        }
        if (op == "options")
        {
            if (catalogs.Problem is string problem)
            {
                return Failed(problem);
            }
            if (field.Type != FieldType.Catalog)
            {
                return Failed($"{field.Label} is not picked from the catalog.");
            }
            List<CatalogEntry> found = editor.Candidates(card, field, string.Empty, out string? why, MaxOptions);
            // Homeland: the race's own province first, then the rest of Tamriel.
            string? native = field.Id == "homeland" ? CardEditor.NativeProvince(editor.RaceName(card)) : null;
            return new
            {
                ok = true,
                field = field.Id,
                options = found
                    .OrderBy(e => native is not null && e.Id == native ? 0 : 1)
                    .Select(e => PageJson.Option(e, native is null ? null : e.Id == native ? NativeLands : OtherProvinces))
                    .ToArray(),
                message = found.Count == 0 ? NeedsFirst(field) ?? why ?? $"Nothing in the catalog fits {field.Label} on this card." : null,
            };
        }
        if (op is not ("set" or "pick" or "vis" or "clear" or "roll" or "keep"))
        {
            return Failed($"Unknown request '{op}'.");
        }
        if (!cards.CanSave(owner))
        {
            return Failed("Your card file could not be read, so it can't be changed right now. The server log says why.");
        }

        object? roll = null;
        EditResult result = op switch
        {
            "set" => field.Type == FieldType.Catalog && mode == CatalogMode.Canon
                ? EditResult.Fail($"{field.Label} is picked from the lore list.")
                : editor.Set(card, field, Text(request, "text")),
            "pick" => Pick(sender, card, catalog, editor, field, Text(request, "id")),
            "vis" => ViewerLevel.TryParse(Text(request, "vis"), out Visibility visibility)
                ? editor.SetVisibility(card, field, visibility)
                : EditResult.Fail("Who should see it: public, known, friends or private?"),
            "roll" => Roll(sender, card, editor, field, out roll),
            "keep" => editor.Keep(card, field),
            _ => editor.Clear(card, field),
        };
        if (result.Ok)
        {
            cards.Save(card);
            logger.LogInformation("MESOL: {Owner} page {Op} {Field}.", owner, op, field.Id);
        }
        return new
        {
            ok = result.Ok,
            message = result.Message,
            card = PageJson.Values(card, text, catalog),
            used = card.TextLength,
            roll,
        };
    }

    /// <summary>A trait roll: checked first, so a roll is never thrown away, then 5d20 through RpRoll.</summary>
    private EditResult Roll(PlayerId sender, CardFile card, CardEditor editor, FieldDefinition field, out object? roll)
    {
        roll = null;
        EditResult allowed = editor.CanRoll(card, field);
        if (!allowed.Ok)
        {
            return allowed;
        }
        int[] values;
        try
        {
            values = RollDice(sender, field.Label);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: rolling {Trait} for {Sender} through RpRoll failed.", field.Id, sender);
            return EditResult.Fail("The dice could not be rolled. Is RpRoll running on this server?");
        }
        EditResult result = editor.ApplyRoll(card, field, values);
        if (result.Ok)
        {
            roll = new { dice = values, total = values.Sum(), reroll = allowed.Message == "reroll" };
            logger.LogInformation("MESOL: {Sender} rolled {Trait}: {Values} = {Total}.", sender, field.Id, string.Join(", ", values), values.Sum());
        }
        return result;
    }

    private static readonly HashSet<string> SocialOps = new(StringComparer.Ordinal)
    {
        "nearby", "view", "introduce", "befriend", "accept", "decline", "forget", "unfriend", "block", "unblock",
    };

    /// <summary>Meeting other players (<see cref="Relations.Social"/>): the Nearby list, their cards, offers and answers.</summary>
    private object RunSocial(PlayerId sender, string op, JsonElement request)
    {
        if (op == "nearby")
        {
            return social.Nearby(sender);
        }
        string who = Text(request, "player");
        if (op == "view")
        {
            return social.View(sender, who);
        }
        if (op == "decline" && who.Length == 0)
        {
            EditResult declined = social.DeclineNewest(sender);
            return new { ok = declined.Ok, message = declined.Message };
        }
        if (!social.Find(who, out PlayerId target))
        {
            return Failed("That player is not here any more.");
        }
        EditResult result = op switch
        {
            "introduce" => social.MakeOffer(sender, target, Relations.Social.Introduce),
            "befriend" => social.MakeOffer(sender, target, Relations.Social.Friend),
            "accept" => social.AcceptFrom(sender, target),
            "decline" => social.DeclineNewest(sender),
            "forget" => social.Forget(sender, target),
            "unfriend" => social.Unfriend(sender, target),
            "block" => social.Block(sender, target),
            _ => social.Unblock(sender, target),
        };
        return new { ok = result.Ok, message = result.Message };
    }

    private EditResult Pick(PlayerId sender, CardFile card, LoreCatalog catalog, CardEditor editor, FieldDefinition field, string id)
    {
        if (!catalog.TryGet(id, out CatalogEntry entry))
        {
            return EditResult.Fail("That entry is not in the lore catalog.");
        }
        if (field.Id == "race" && config.RaceLock && SafeRaceOf(sender) is string gameRace && gameRace != entry.Id)
        {
            return EditResult.Fail($"Your character is a {catalog.NameOf(gameRace)} in the game, and this server keeps the card's race the same.");
        }
        return editor.Pick(card, field, entry);
    }

    private object? RaceHint(PlayerId sender, CardFile card, LoreCatalog catalog)
    {
        if (!CardFields.TryGet("race", out FieldDefinition raceField) || card.Get(raceField)?.CatalogId is not null)
        {
            return null;
        }
        return SafeRaceOf(sender) is string id && catalog.TryGet(id, out CatalogEntry entry)
            ? new { id = entry.Id, name = entry.Name }
            : null;
    }

    private static string? NeedsFirst(FieldDefinition field) => field.Id switch
    {
        "subtype" or "culture" => "Pick your race first.",
        _ => null,
    };

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

    private static string Text(JsonElement request, string name)
    {
        return request.ValueKind == JsonValueKind.Object
            && request.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static object Failed(string message) => new { ok = false, message };

    private static string Fail(string message) => Serialize(Failed(message));

    private static string Serialize(object value) => PageJson.Serialize(value);
}
