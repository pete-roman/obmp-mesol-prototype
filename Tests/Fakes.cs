using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MESOL.Server.Catalog;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Players;

namespace MESOL.Tests;

internal static class Fakes
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);

    internal static PlayerId Player(ushort id) => (PlayerId)typeof(PlayerId)
        .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(ushort) })!
        .Invoke(new object[] { id });

    /// <summary>A small catalog with one entry of each case the rules care about.</summary>
    internal const string SmallCatalogJson = """
    {
      "entries": [
        { "id": "race.khajiit", "race": "Khajiit", "kind": "race", "name": "Khajiit", "era": "current" },
        { "id": "race.nord", "race": "Nord", "kind": "race", "name": "Nord", "era": "current" },
        { "id": "khajiit.furstock.cathay-raht", "race": "Khajiit", "kind": "subtype", "name": "Cathay-raht", "era": "current", "modelFit": "plausible" },
        { "id": "khajiit.furstock.cathay", "race": "Khajiit", "kind": "subtype", "name": "Cathay", "era": "current", "modelFit": "plausible" },
        { "id": "khajiit.furstock.senche", "race": "Khajiit", "kind": "subtype", "name": "Senche", "era": "current", "modelFit": "implausible" },
        { "id": "khajiit.region.anequina", "race": "Khajiit", "kind": "region", "name": "Anequina", "parent": "province.elsweyr", "era": "current", "remasteredOrigin": true },
        { "id": "settlement.rimmen", "race": null, "kind": "settlement", "name": "Rimmen", "parent": "khajiit.region.anequina", "era": "current" },
        { "id": "province.elsweyr", "race": null, "kind": "province", "name": "Elsweyr", "era": "current" },
        { "id": "province.skyrim", "race": null, "kind": "province", "name": "Skyrim", "era": "current" },
        { "id": "region.heartlands", "race": null, "kind": "region", "name": "Heartlands", "parent": "province.cyrodiil", "era": "current" },
        { "id": "upbringing.temple", "race": null, "kind": "upbringing", "name": "Raised by a temple", "era": "current" },
        { "id": "khajiit.upbringing.caravan", "race": "Khajiit", "kind": "upbringing", "name": "Born to a caravan", "era": "current" },
        { "id": "nord.upbringing.hold", "race": "Nord", "kind": "upbringing", "name": "Raised in a Hold", "era": "current" },
        { "id": "nord.heritage.atmoran", "race": "Nord", "kind": "heritage", "name": "Atmoran descent", "era": "historical", "modelFit": "plausible" },
        { "id": "province.cyrodiil", "race": null, "kind": "province", "name": "Cyrodiil", "era": "current" },
        { "id": "faction.fighters-guild", "race": null, "kind": "faction", "name": "Fighters Guild", "era": "current" },
        { "id": "faction.mages-guild", "race": null, "kind": "faction", "name": "Mages Guild", "era": "current" },
        { "id": "faction.daggerfall-covenant", "race": null, "kind": "faction", "name": "Daggerfall Covenant", "era": "historical" },
        { "id": "faction.third-aldmeri-dominion", "race": null, "kind": "faction", "name": "Third Aldmeri Dominion", "era": "future" },
        { "id": "rank.fighters-guild.associate", "race": null, "kind": "rank", "name": "Associate", "parent": "faction.fighters-guild", "era": "current" },
        { "id": "rank.mages-guild.apprentice", "race": null, "kind": "rank", "name": "Apprentice", "parent": "faction.mages-guild", "era": "current" },
        { "id": "birthsign.the-thief", "race": null, "kind": "birthsign", "name": "The Thief", "era": "current" },
        { "id": "mixed.cyro-nord", "race": "Mixed", "kind": "heritage", "name": "Cyro-Nord", "era": "current" }
      ]
    }
    """;

    internal static LoreCatalog SmallCatalog() => LoreCatalog.Load(SmallCatalogJson, Array.Empty<(string, string)>(), new List<string>());

    internal static string ShippedCatalogPath => Path.Combine(AppContext.BaseDirectory, "catalog", "mesol-lore.json");

    /// <summary>The MESOL folder of the repository (the one with MESOL.slnx), found from the test's output folder.</summary>
    internal static string RepoMesol
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MESOL.slnx")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new DirectoryNotFoundException("MESOL.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    internal static string TempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mesol-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}

internal sealed class FakeChat : IChat
{
    public readonly List<(PlayerId Player, string Text, ChatLineKind Kind)> Sent = new();

    public List<ChatPlayer> Players { get; } = new();

    public IReadOnlyList<ChatPlayer> ConnectedPlayers => Players;

    public string GetNickname(PlayerId player) => Players.FirstOrDefault(p => p.Id == player)?.Nickname ?? string.Empty;

    public void Broadcast(string text, ChatLineKind kind = ChatLineKind.System) => Sent.Add((default, text, kind));

    public void SendTo(PlayerId player, string text, ChatLineKind kind = ChatLineKind.System) => Sent.Add((player, text, kind));

    public void AddCommand(string name, string usage, ChatCommandHandler run)
    {
    }

    public List<string> TextsTo(PlayerId player) => Sent.Where(s => s.Player == player).Select(s => s.Text).ToList();
}

internal sealed class FakePlayers : IPlayers
{
    public readonly Dictionary<PlayerId, Guid> Accounts = new();

    public void Kick(PlayerId player)
    {
    }

    public Guid? GetReadyMId(PlayerId player) => Accounts.TryGetValue(player, out Guid id) ? id : null;
}
