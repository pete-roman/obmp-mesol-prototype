using System;
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
/// /lore [player] [field]: another character's card, cut down on the server to what this viewer may see.
/// What the viewer sees depends on introductions and friendships (<see cref="Relations.Social"/>). With the card page loaded,
/// the whole card opens there (the same cut); one field (/lore 3 history) still answers in chat.
/// </summary>
[Service]
public sealed partial class LoreCommand(
    IChat chat, IPlayers players, CardStore cards, CatalogService catalogs, Targets targets, Page.PageLink page, Relations.Social social,
    MesolConfig config, ILogger logger)
{
    internal const string Usage = "/lore [id] [field] - the lore card of the player in front of you, or of a /who id, as they let you see it";

    /// <summary>Finds the player a command means; replaced in the tests.</summary>
    internal TryFindTarget FindTarget { get; set; } = targets.TryFind;

    internal delegate bool TryFindTarget(PlayerId sender, string who, float rangeMeters, out PlayerId target, out string problem);

    private void Start()
    {
        chat.AddCommand("lore", Usage, Run);
        logger.LogInformation("MESOL: /lore registered, looks {Range} m ahead.", config.LookRangeMeters);
    }

    /// <summary>The /lore handler. Never throws.</summary>
    internal void Run(IChat chat, PlayerId sender, string arguments)
    {
        try
        {
            Handle(chat, sender, arguments ?? string.Empty);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: '/lore {Arguments}' from {Sender} failed.", arguments, sender);
            Send(chat, sender, "That didn't work. Try again.", ChatLineKind.Rejection);
        }
    }

    private void Handle(IChat chat, PlayerId sender, string arguments)
    {
        // The last word may name a field: "/lore 3 history" or "/lore history" (the player in front of you).
        string who = arguments.Trim();
        FieldDefinition? field = null;
        int space = who.LastIndexOf(' ');
        string last = space < 0 ? who : who[(space + 1)..];
        if (CardFields.TryGet(last, out FieldDefinition named))
        {
            field = named;
            who = space < 0 ? string.Empty : who[..space].Trim();
        }

        if (!FindTarget(sender, who, config.LookRangeMeters, out PlayerId target, out string problem))
        {
            Send(chat, sender, problem, ChatLineKind.Rejection);
            return;
        }
        string nickname = chat.GetNickname(target);
        if (players.GetReadyMId(target) is not Guid owner)
        {
            Send(chat, sender, $"{nickname}'s card isn't available yet.", ChatLineKind.CommandReply);
            return;
        }
        CardFile? card = cards.Find(owner);
        Visibility level = social.LevelOf(sender, owner);
        CardText text = new(catalogs.Current, markCustom: CardEditor.ParseMode(config.CatalogMode) == CatalogMode.CanonPlus);
        if (field is null && page.HasPage(sender))
        {
            page.Push(sender, social.ViewPush(sender, target));
            return;
        }
        if (card is null || card.IsEmpty)
        {
            Send(chat, sender, $"{nickname} has no lore card yet.", ChatLineKind.CommandReply);
            return;
        }

        if (field is not null)
        {
            // The same answer for "empty" and "not for you", so a viewer can't tell hidden fields from missing ones.
            if (!CardView.CanSee(card, field, level))
            {
                Send(chat, sender, $"Nothing to show for {field.Label}.", ChatLineKind.CommandReply);
                return;
            }
            foreach (string line in text.Full(field, card.Get(field)!))
            {
                Send(chat, sender, line, ChatLineKind.CommandReply);
            }
            return;
        }
        foreach (string line in text.Lines(card, level, showVisibility: false, moreHint: $"/lore {target.RawValue} <field> shows a long field in full."))
        {
            Send(chat, sender, line, ChatLineKind.CommandReply);
        }
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
