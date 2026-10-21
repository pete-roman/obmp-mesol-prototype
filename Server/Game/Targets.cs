using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OblivionMp.ChatMod.Serverside;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace MESOL.Server.Game;

/// <summary>
/// Which player a command means. There is no look-at or crosshair API in OB:MP 1.0, so "the player in front of
/// you" is the nearest connected player in the same area within range. A /who id or a nickname names one exactly.
/// </summary>
[Service]
public sealed partial class Targets(IChat chat, IEntities entities)
{
    /// <summary>
    /// Finds the player meant by <paramref name="who"/>: empty for the nearest one within
    /// <paramref name="rangeMeters"/>, a /who id, or a nickname. On failure <paramref name="problem"/> says why.
    /// </summary>
    public bool TryFind(PlayerId sender, string who, float rangeMeters, out PlayerId target, out string problem)
    {
        target = default;
        problem = string.Empty;
        string text = (who ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return TryNearest(sender, rangeMeters, out target, out problem);
        }
        ChatPlayer? found = ushort.TryParse(text, out ushort rawId)
            ? chat.ConnectedPlayers.FirstOrDefault(p => p.Id.RawValue == rawId)
            : chat.ConnectedPlayers.FirstOrDefault(p => string.Equals(p.Nickname, text, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            problem = $"There is no player '{text}'. /who shows the ids.";
            return false;
        }
        target = found.Id;
        return true;
    }

    /// <summary>
    /// Every other connected player in the sender's area within <paramref name="rangeMeters"/>, nearest first, with the
    /// distance in metres. Empty when the sender has no character in the world.
    /// </summary>
    public List<(PlayerId Player, float Meters)> Around(PlayerId sender, float rangeMeters)
    {
        List<(PlayerId, float)> around = new();
        if (!entities.TryLookup<MainCharacter, PlayerId>(sender, out MainCharacter self)
            || !entities.TryGetScope<MainCharacter, Area>(in self, out Area selfArea))
        {
            return around;
        }
        foreach (ChatPlayer player in chat.ConnectedPlayers)
        {
            if (player.Id != sender
                && entities.TryLookup<MainCharacter, PlayerId>(player.Id, out MainCharacter other)
                && entities.TryGetScope<MainCharacter, Area>(in other, out Area otherArea)
                && otherArea.AreaId == selfArea.AreaId)
            {
                float meters = Vector3.Distance(self.Position, other.Position) / 100f; // Unreal centimetres
                if (meters <= rangeMeters)
                {
                    around.Add((player.Id, meters));
                }
            }
        }
        return around.OrderBy(p => p.Item2).ToList();
    }

    private bool TryNearest(PlayerId sender, float rangeMeters, out PlayerId target, out string problem)
    {
        target = default;
        if (!entities.TryLookup<MainCharacter, PlayerId>(sender, out MainCharacter self)
            || !entities.TryGetScope<MainCharacter, Area>(in self, out Area selfArea))
        {
            problem = "Your character is not in the world yet. Try again in a moment, or use a /who id.";
            return false;
        }
        // Positions are Unreal centimetres.
        float best = rangeMeters * 100f;
        bool any = false;
        foreach (ChatPlayer player in chat.ConnectedPlayers)
        {
            if (player.Id == sender)
            {
                continue;
            }
            // Different areas (another interior or worldspace) are never near, even if the coordinates are close.
            if (entities.TryLookup<MainCharacter, PlayerId>(player.Id, out MainCharacter other)
                && entities.TryGetScope<MainCharacter, Area>(in other, out Area otherArea)
                && otherArea.AreaId == selfArea.AreaId)
            {
                float distance = Vector3.Distance(self.Position, other.Position);
                if (distance <= best)
                {
                    best = distance;
                    target = player.Id;
                    any = true;
                }
            }
        }
        problem = any ? string.Empty : $"Nobody is within {rangeMeters:0.#} m of you. Use a /who id instead.";
        return any;
    }
}
