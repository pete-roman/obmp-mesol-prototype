using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace MESOL.Server.Game;

/// <summary>
/// The race a character has in the game, from <c>MainCharacter.RacePath</c> (an Unreal asset path, reported by the
/// player's game). Only a suggestion for the card: on OB:MP 1.0.0-rc.2 the server does not save race yet.
/// Every new path is logged once, so the mapping can be checked against what the game really sends.
/// </summary>
[Service]
public sealed partial class GameRace(IEntities entities, ILogger logger)
{
    // Words in the asset path, in Oblivion's own race names and the lore names. First match wins.
    private static readonly (string Word, string RaceId)[] Words =
    {
        ("argonian", "race.argonian"),
        ("breton", "race.breton"),
        ("darkelf", "race.dunmer"),
        ("dunmer", "race.dunmer"),
        ("highelf", "race.altmer"),
        ("altmer", "race.altmer"),
        ("woodelf", "race.bosmer"),
        ("bosmer", "race.bosmer"),
        ("imperial", "race.imperial"),
        ("khajiit", "race.khajiit"),
        ("nord", "race.nord"),
        ("orsimer", "race.orsimer"),
        ("orc", "race.orsimer"),
        ("redguard", "race.redguard"),
    };

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>The catalog race id of the player's character, or null (no character in the world, or an unknown path).</summary>
    public string? RaceIdOf(PlayerId player)
    {
        if (!entities.TryLookup<MainCharacter, PlayerId>(player, out MainCharacter character))
        {
            return null;
        }
        string path = character.RacePath.ToString();
        string? raceId = Map(path);
        if (_seen.Add(path))
        {
            logger.LogInformation("MESOL: race path '{Path}' maps to {Race}.", path, raceId ?? "nothing (unknown)");
        }
        return raceId;
    }

    internal static string? Map(string path)
    {
        foreach (string token in Regex.Split((path ?? string.Empty).ToLowerInvariant(), "[^a-z]+"))
        {
            foreach ((string word, string raceId) in Words)
            {
                if (token.StartsWith(word, StringComparison.Ordinal))
                {
                    return raceId;
                }
            }
        }
        return null;
    }
}
