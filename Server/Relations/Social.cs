using System;
using System.Collections.Generic;
using System.Linq;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using MESOL.Server.Page;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Players;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Relations;

/// <summary>
/// Meeting other characters: who is nearby, what of their card you may see, and the handshake that changes it.
/// An introduction or a friendship is an offer that the other player accepts (= in game, or on the card page) within
/// <see cref="OfferSeconds"/>; only then does anything change, on both sides at once.
/// Offers live in memory only: a restart simply drops them.
/// </summary>
[Service]
public sealed partial class Social(
    IPlayers players, IChat chat, CardStore cards, RelationStore relations, CatalogService catalogs, Targets targets,
    PageLink page, MesolConfig config, ILogger logger)
{
    public const float NearbyMeters = 20f;

    public const int OfferSeconds = 30;

    public const string Introduce = "introduce";

    public const string Friend = "friend";

    private sealed record Offer(long Id, string Kind, PlayerId From, PlayerId To, Guid FromAccount, Guid ToAccount, DateTimeOffset Expires);

    private readonly List<Offer> _offers = new();
    private long _nextOfferId = 1;

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Players around a player, nearest first; replaced in the tests.</summary>
    internal Func<PlayerId, float, List<(PlayerId Player, float Meters)>> Around { get; set; } = targets.Around;

    // ---- seeing ----

    /// <summary>The level of <paramref name="owner"/>'s card that <paramref name="viewer"/> may see.</summary>
    public Visibility LevelOf(PlayerId viewer, Guid owner)
    {
        return players.GetReadyMId(viewer) is Guid me ? relations.LevelOf(me, owner) : Visibility.Public;
    }

    /// <summary>The players within 20 m, each as this viewer may see them.</summary>
    public object Nearby(PlayerId sender)
    {
        if (players.GetReadyMId(sender) is not Guid me)
        {
            return new { ok = false, message = "The server doesn't know your ReadyM account yet. Try again in a moment." };
        }
        Expire();
        CardText text = Text();
        List<object> list = new();
        foreach ((PlayerId other, float meters) in Around(sender, NearbyMeters))
        {
            if (players.GetReadyMId(other) is not Guid account)
            {
                continue;
            }
            Relation relation = relations.Between(me, account);
            CardFile? card = cards.Find(account);
            list.Add(new
            {
                id = other.RawValue.ToString(),
                header = Header(card, relations.LevelOf(me, account), other, text),
                meters = Math.Round(meters, 1),
                relation = Word(relation),
                hasCard = card is { IsEmpty: false },
                incoming = _offers.Where(o => o.From == other && o.To == sender).Select(o => o.Kind).FirstOrDefault(),
                outgoing = _offers.Where(o => o.From == sender && o.To == other).Select(o => o.Kind).FirstOrDefault(),
            });
        }
        return new { ok = true, players = list, range = NearbyMeters };
    }

    /// <summary>Another player's card, cut to what the viewer may see, for the card page.</summary>
    public object View(PlayerId sender, string who)
    {
        if (!Find(who, out PlayerId target) || players.GetReadyMId(target) is not Guid account || players.GetReadyMId(sender) is not Guid me)
        {
            return new { ok = false, message = "That player is not here any more." };
        }
        CardText text = Text();
        Visibility level = relations.LevelOf(me, account);
        CardFile? card = cards.Find(account);
        return new
        {
            ok = true,
            id = target.RawValue.ToString(),
            who = Header(card, level, target, text),
            relation = Word(relations.Between(me, account)),
            view = card is null || card.IsEmpty ? null : PageJson.View(card, level, text),
        };
    }

    // ---- offers: introduce, befriend ----

    /// <summary>Offers <paramref name="kind"/> to <paramref name="target"/>; the target's screen shows it.</summary>
    public EditResult MakeOffer(PlayerId sender, PlayerId target, string kind)
    {
        Expire();
        if (target == sender)
        {
            return EditResult.Fail("That is you.");
        }
        if (players.GetReadyMId(sender) is not Guid me || players.GetReadyMId(target) is not Guid them)
        {
            return EditResult.Fail("That player's account isn't known to the server yet. Try again in a moment.");
        }
        Relation relation = relations.Between(me, them);
        CardText text = Text();
        string theirName = Header(cards.Find(them), relations.LevelOf(me, them), target, text);
        if (relation == Relation.Blocked)
        {
            return EditResult.Fail("You can't make offers to this player.");
        }
        if (kind == Introduce && relation is Relation.Known or Relation.Friend)
        {
            return EditResult.Fail($"You already know {theirName}.");
        }
        if (kind == Friend && relation == Relation.Friend)
        {
            return EditResult.Fail($"You and {theirName} are friends already.");
        }
        if (_offers.Any(o => o.From == sender && o.To == target && o.Kind == kind))
        {
            return EditResult.Fail($"You already asked {theirName}. Wait for an answer.");
        }
        // Both asked each other: that is a yes from both.
        Offer? theirs = _offers.FirstOrDefault(o => o.From == target && o.To == sender && o.Kind == kind);
        if (theirs is not null)
        {
            return Accept(theirs);
        }
        Offer offer = new(_nextOfferId++, kind, sender, target, me, them, Clock().AddSeconds(OfferSeconds));
        _offers.Add(offer);
        string myName = Header(cards.Find(me), relations.LevelOf(them, me), sender, text);
        page.Panel(target, PageJson.Serialize(new
        {
            op = "offer",
            id = offer.Id,
            kind,
            from = myName,
            seconds = OfferSeconds,
            text = kind == Introduce ? $"{Capital(myName)} wants to introduce themselves." : $"{Capital(myName)} asks to be friends.",
        }));
        string asked = kind == Introduce ? $"You offered to introduce yourself to {theirName}." : $"You asked {theirName} to be friends.";
        Note(sender, asked + " Waiting for an answer.");
        logger.LogInformation("MESOL: {From} offers {Kind} to {To}.", me, kind, them);
        return EditResult.Done(asked);
    }

    /// <summary>Accepts the newest offer made to <paramref name="sender"/>.</summary>
    public EditResult AcceptNewest(PlayerId sender)
    {
        Expire();
        Offer? offer = _offers.LastOrDefault(o => o.To == sender);
        return offer is null ? EditResult.Fail("Nobody is waiting for your answer.") : Accept(offer);
    }

    /// <summary>Accepts the offer from one player, if there is one.</summary>
    public EditResult AcceptFrom(PlayerId sender, PlayerId from)
    {
        Expire();
        Offer? offer = _offers.LastOrDefault(o => o.To == sender && o.From == from);
        return offer is null ? EditResult.Fail("They are not waiting for your answer.") : Accept(offer);
    }

    public EditResult DeclineNewest(PlayerId sender)
    {
        Expire();
        Offer? offer = _offers.LastOrDefault(o => o.To == sender);
        if (offer is null)
        {
            return EditResult.Fail("Nobody is waiting for your answer.");
        }
        _offers.Remove(offer);
        page.Panel(sender, PageJson.Serialize(new { op = "clear", id = offer.Id }));
        CardText text = Text();
        Note(offer.From, $"{Capital(Header(cards.Find(offer.ToAccount), relations.LevelOf(offer.FromAccount, offer.ToAccount), offer.To, text))} said no.");
        return EditResult.Done("Declined.");
    }

    private EditResult Accept(Offer offer)
    {
        _offers.RemoveAll(o => o.Kind == offer.Kind
            && ((o.From == offer.From && o.To == offer.To) || (o.From == offer.To && o.To == offer.From)));
        if (relations.Between(offer.ToAccount, offer.FromAccount) == Relation.Blocked)
        {
            return EditResult.Fail("That offer can't be accepted.");
        }
        if (offer.Kind == Friend)
        {
            relations.Befriend(offer.FromAccount, offer.ToAccount);
        }
        else
        {
            relations.Introduce(offer.FromAccount, offer.ToAccount);
        }
        page.Panel(offer.To, PageJson.Serialize(new { op = "clear", id = offer.Id }));
        CardText text = Text();
        // Now each one sees the other's new level: the names show.
        string fromName = Header(cards.Find(offer.FromAccount), relations.LevelOf(offer.ToAccount, offer.FromAccount), offer.From, text);
        string toName = Header(cards.Find(offer.ToAccount), relations.LevelOf(offer.FromAccount, offer.ToAccount), offer.To, text);
        string done = offer.Kind == Friend ? "You and {0} are friends now." : "You and {0} know each other now.";
        Note(offer.To, string.Format(done, fromName));
        Note(offer.From, string.Format(done, toName));
        Say(offer.To, string.Format(done, fromName));
        Say(offer.From, string.Format(done, toName));
        logger.LogInformation("MESOL: {From} and {To}: {Kind} accepted.", offer.FromAccount, offer.ToAccount, offer.Kind);
        return EditResult.Done(string.Format(done, fromName));
    }

    // ---- taking it back ----

    public EditResult Forget(PlayerId sender, PlayerId target) => Change(sender, target, (me, them, name) =>
        relations.Forget(me, them) ? EditResult.Done($"You and {name} are strangers again.") : EditResult.Fail($"You don't know {name}."));

    public EditResult Unfriend(PlayerId sender, PlayerId target) => Change(sender, target, (me, them, name) =>
        relations.Unfriend(me, them) ? EditResult.Done($"You and {name} are no longer friends.") : EditResult.Fail($"You and {name} are not friends."));

    public EditResult Block(PlayerId sender, PlayerId target) => Change(sender, target, (me, them, name) =>
    {
        relations.Block(me, them);
        _offers.RemoveAll(o => (o.From == sender && o.To == target) || (o.From == target && o.To == sender));
        return EditResult.Done($"Blocked {name}: they see only your public fields and can't make you offers.");
    });

    public EditResult Unblock(PlayerId sender, PlayerId target) => Change(sender, target, (me, them, name) =>
        relations.Unblock(me, them) ? EditResult.Done($"Unblocked {name}.") : EditResult.Fail($"{name} is not blocked."));

    private EditResult Change(PlayerId sender, PlayerId target, Func<Guid, Guid, string, EditResult> change)
    {
        if (players.GetReadyMId(sender) is not Guid me || players.GetReadyMId(target) is not Guid them)
        {
            return EditResult.Fail("That player's account isn't known to the server yet.");
        }
        string name = Header(cards.Find(them), relations.LevelOf(me, them), target, Text());
        EditResult result = change(me, them, name);
        if (result.Ok)
        {
            logger.LogInformation("MESOL: {Me} and {Them}: {Result}", me, them, result.Message);
        }
        return result;
    }

    // ---- keys in the game (=, -, Shift+End) ----

    /// <summary>
    /// = : accept the newest offer, or offer an introduction to the player in front. - : decline the newest offer.
    /// look (Shift+End): open the card of the player in front.
    /// </summary>
    public void Gesture(PlayerId sender, string what)
    {
        try
        {
            switch (what)
            {
                case "introduce":
                    Expire();
                    if (_offers.Any(o => o.To == sender))
                    {
                        AcceptNewest(sender);
                        return;
                    }
                    if (!targets.TryFind(sender, string.Empty, config.LookRangeMeters, out PlayerId front, out string problem))
                    {
                        Note(sender, "Nobody is close enough to introduce yourself to. Stand in front of them.");
                        return;
                    }
                    EditResult offered = MakeOffer(sender, front, Introduce);
                    if (!offered.Ok)
                    {
                        Note(sender, offered.Message);
                    }
                    return;
                case "decline":
                    DeclineNewest(sender);
                    return;
                case "look":
                    if (!targets.TryFind(sender, string.Empty, config.LookRangeMeters, out PlayerId seen, out string why))
                    {
                        Note(sender, "Nobody is close enough. Stand in front of them, or open Nearby on your card (End).");
                        return;
                    }
                    page.Push(sender, ViewPush(sender, seen));
                    return;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: gesture '{What}' from {Sender} failed.", what, sender);
        }
    }

    /// <summary>The server's own message that opens another player's card on the sender's page.</summary>
    public string ViewPush(PlayerId viewer, PlayerId target)
    {
        CardText text = Text();
        Guid? account = players.GetReadyMId(target);
        CardFile? card = account is Guid a ? cards.Find(a) : null;
        Visibility level = account is Guid owner ? LevelOf(viewer, owner) : Visibility.Public;
        return PageJson.Serialize(new
        {
            op = "view",
            id = target.RawValue.ToString(),
            who = Header(card, level, target, text),
            relation = account is Guid o2 && players.GetReadyMId(viewer) is Guid me ? Word(relations.Between(me, o2)) : "stranger",
            view = card is null || card.IsEmpty ? null : PageJson.View(card, level, text),
        });
    }

    /// <summary>Offers nobody answered in time are dropped, and both sides hear of it.</summary>
    private void Update()
    {
        Expire();
    }

    private void Expire()
    {
        DateTimeOffset now = Clock();
        foreach (Offer offer in _offers.Where(o => o.Expires <= now).ToList())
        {
            _offers.Remove(offer);
            page.Panel(offer.To, PageJson.Serialize(new { op = "clear", id = offer.Id }));
            Note(offer.From, "No answer to your offer.");
        }
    }

    // ---- helpers ----

    public bool Find(string who, out PlayerId player)
    {
        ChatPlayer? found = ushort.TryParse((who ?? string.Empty).Trim(), out ushort raw)
            ? chat.ConnectedPlayers.FirstOrDefault(p => p.Id.RawValue == raw)
            : null;
        player = found?.Id ?? default;
        return found is not null;
    }

    /// <summary>The name line a viewer sees: from the card at that level, or the player's nickname when there is no card.</summary>
    private string Header(CardFile? card, Visibility level, PlayerId player, CardText text)
    {
        if (card is null || card.IsEmpty)
        {
            string nickname = chat.GetNickname(player);
            return nickname.Length > 0 ? nickname : "Someone";
        }
        return text.Header(card, level);
    }

    private CardText Text() => new(catalogs.Current, markCustom: CardEditor.ParseMode(config.CatalogMode) == CatalogMode.CanonPlus);

    private void Note(PlayerId player, string text) => page.Panel(player, PageJson.Serialize(new { op = "note", text }));

    private void Say(PlayerId player, string text)
    {
        try
        {
            chat.SendTo(player, text, ChatLineKind.CommandReply);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: could not send a line to {Player}.", player);
        }
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string Word(Relation relation) => relation switch
    {
        Relation.Known => "known",
        Relation.Friend => "friend",
        Relation.Blocked => "blocked",
        _ => "stranger",
    };
}
