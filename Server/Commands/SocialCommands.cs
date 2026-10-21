using System;
using MESOL.Server.Cards;
using MESOL.Server.Game;
using MESOL.Server.Relations;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Commands;

/// <summary>
/// /introduce and /friend: the same offers and answers as = and the card page's Nearby section, for chat.
/// </summary>
[Service]
public sealed partial class SocialCommands(IChat chat, Social social, Targets targets, MesolConfig config, ILogger logger)
{
    internal const string IntroduceUsage = "/introduce [id|accept|decline] - offer to introduce yourself to the player in front of you (or press =)";

    internal const string FriendUsage = "/friend add|accept|decline|remove|forget|block|unblock [id] - friendships and blocks";

    /// <summary>Finds the player a command means; replaced in the tests.</summary>
    internal LoreCommand.TryFindTarget FindTarget { get; set; } = targets.TryFind;

    private void Start()
    {
        chat.AddCommand("introduce", IntroduceUsage, (c, sender, args) => Guard(c, sender, () => Introduce(c, sender, args ?? string.Empty)));
        chat.AddCommand("friend", FriendUsage, (c, sender, args) => Guard(c, sender, () => Friend(c, sender, args ?? string.Empty)));
        logger.LogInformation("MESOL: /introduce and /friend registered; = and - answer offers in game.");
    }

    internal void Introduce(IChat chat, PlayerId sender, string arguments)
    {
        string arg = arguments.Trim().ToLowerInvariant();
        if (arg == "accept")
        {
            Reply(chat, sender, social.AcceptNewest(sender));
            return;
        }
        if (arg == "decline")
        {
            Reply(chat, sender, social.DeclineNewest(sender));
            return;
        }
        if (!FindTarget(sender, arg, config.LookRangeMeters, out PlayerId target, out string problem))
        {
            Send(chat, sender, problem, ChatLineKind.Rejection);
            return;
        }
        Reply(chat, sender, social.MakeOffer(sender, target, Social.Introduce));
    }

    internal void Friend(IChat chat, PlayerId sender, string arguments)
    {
        string trimmed = arguments.Trim();
        int space = trimmed.IndexOf(' ');
        string verb = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        string who = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        if (verb is "accept" or "decline")
        {
            Reply(chat, sender, verb == "accept" ? social.AcceptNewest(sender) : social.DeclineNewest(sender));
            return;
        }
        if (verb is not ("add" or "remove" or "forget" or "block" or "unblock"))
        {
            Send(chat, sender, FriendUsage, ChatLineKind.CommandReply);
            return;
        }
        if (!FindTarget(sender, who, config.LookRangeMeters, out PlayerId target, out string problem))
        {
            Send(chat, sender, problem, ChatLineKind.Rejection);
            return;
        }
        Reply(chat, sender, verb switch
        {
            "add" => social.MakeOffer(sender, target, Social.Friend),
            "remove" => social.Unfriend(sender, target),
            "forget" => social.Forget(sender, target),
            "block" => social.Block(sender, target),
            _ => social.Unblock(sender, target),
        });
    }

    private void Reply(IChat chat, PlayerId sender, EditResult result) =>
        Send(chat, sender, result.Message, result.Ok ? ChatLineKind.CommandReply : ChatLineKind.Rejection);

    private void Guard(IChat chat, PlayerId sender, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: a social command from {Sender} failed.", sender);
            Send(chat, sender, "That didn't work. Try again.", ChatLineKind.Rejection);
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
