using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MESOL.Server;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using MESOL.Server.Page;
using MESOL.Server.Relations;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using Xunit;

namespace MESOL.Tests;

/// <summary>
/// Meeting other players: Nearby, cards cut by relation, and the handshake. Nothing changes until the other player
/// accepts; then it changes for both. Blocks, taking things back and restarts.
/// </summary>
public class SocialTests
{
    private static readonly PlayerId Pete = Fakes.Player(1);
    private static readonly PlayerId Marcus = Fakes.Player(2);
    private static readonly PlayerId Dravyn = Fakes.Player(3);

    private sealed class Setup
    {
        public readonly FakeChat Chat = new();
        public readonly FakePlayers Players = new();
        public readonly CardStore Cards = StoreTests.Store(Fakes.TempFolder());
        public readonly MesolConfig Config = new();
        public readonly PageLink Page = new();
        public readonly string RelationsFolder = Fakes.TempFolder();
        public readonly List<(PlayerId Player, long Id, JsonElement Json)> Sent = new();
        public readonly CatalogService Catalogs;
        public RelationStore Relations;
        public Social Social;
        public DateTimeOffset Now = Fakes.Now;

        public Setup()
        {
            foreach ((PlayerId id, string name) in new[] { (Pete, "Pete"), (Marcus, "Marcus"), (Dravyn, "Dravyn") })
            {
                Chat.Players.Add(new ChatPlayer(id, name));
                Players.Accounts[id] = Guid.NewGuid();
            }
            MesolData data = new(new Mod(NullLogger.Instance), Config);
            Catalogs = new CatalogService(data, NullLogger.Instance);
            Catalogs.Use(Fakes.SmallCatalog());
            Page.Send = (player, id, json) => Sent.Add((player, id, JsonDocument.Parse(json).RootElement));
            Relations = NewRelations();
            Social = NewSocial();
            Card(Pete, ("firstName", "Ri'Zahra"), ("seenAs", "a scarred Khajiit"), ("history", "Born in Rimmen."));
            Card(Marcus, ("firstName", "Marcus"), ("seenAs", "a broad Imperial"));
        }

        public RelationStore NewRelations() => new(new MesolData(new Mod(NullLogger.Instance), Config), NullLogger.Instance)
        {
            FolderOverride = RelationsFolder,
            Clock = () => Now,
        };

        public Social NewSocial() => new(Players, Chat, Cards, Relations, Catalogs, new Targets(Chat, null!), Page, Config, NullLogger.Instance)
        {
            Clock = () => Now,
            Around = (sender, _) => Chat.Players.Where(p => p.Id != sender).Select((p, i) => (p.Id, 2f + i * 5f)).ToList(),
        };

        private void Card(PlayerId player, params (string Field, string Text)[] fields)
        {
            CardFile card = Cards.GetOrCreate(Players.Accounts[player]);
            CardEditor editor = new(Catalogs.Current, CatalogMode.Canon, false, () => Now);
            foreach ((string id, string text) in fields)
            {
                CardFields.TryGet(id, out FieldDefinition field);
                Assert.True(editor.Set(card, field, text).Ok);
            }
            Cards.Save(card);
        }

        public Guid Account(PlayerId p) => Players.Accounts[p];

        public JsonElement Nearby(PlayerId viewer) => JsonDocument.Parse(PageJson.Serialize(Social.Nearby(viewer))).RootElement;

        public string HeaderSeenBy(PlayerId viewer, PlayerId who) => Nearby(viewer).GetProperty("players").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == who.RawValue.ToString()).GetProperty("header").GetString()!;

        public List<JsonElement> PanelsTo(PlayerId player) => Sent.Where(s => s.Player == player && s.Id == -1).Select(s => s.Json).ToList();
    }

    [Fact]
    public void StrangersSeeTheSeenAsLine()
    {
        Setup s = new();

        Assert.Equal("a scarred Khajiit", s.HeaderSeenBy(Marcus, Pete));
        Assert.Equal("Dravyn", s.HeaderSeenBy(Pete, Dravyn)); // no card: the nickname
        Assert.Equal(Visibility.Public, s.Social.LevelOf(Marcus, s.Account(Pete)));
    }

    [Fact]
    public void AnOfferChangesNothingUntilAccepted()
    {
        Setup s = new();

        Assert.True(s.Social.MakeOffer(Pete, Marcus, Social.Introduce).Ok);

        Assert.Equal(Visibility.Public, s.Social.LevelOf(Marcus, s.Account(Pete)));
        JsonElement offer = Assert.Single(s.PanelsTo(Marcus));
        Assert.Equal("offer", offer.GetProperty("op").GetString());
        Assert.Equal("A scarred Khajiit wants to introduce themselves.", offer.GetProperty("text").GetString());
        Assert.Contains(s.PanelsTo(Pete), p => p.GetProperty("text").GetString()!.Contains("Waiting for an answer"));
        Assert.Equal("introduce", s.Nearby(Marcus).GetProperty("players")[0].GetProperty("incoming").GetString());
    }

    [Fact]
    public void AcceptingIntroducesBothWays()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Introduce);

        Assert.True(s.Social.AcceptNewest(Marcus).Ok);

        Assert.Equal(Visibility.Known, s.Social.LevelOf(Marcus, s.Account(Pete)));
        Assert.Equal(Visibility.Known, s.Social.LevelOf(Pete, s.Account(Marcus)));
        Assert.Equal("Ri'Zahra", s.HeaderSeenBy(Marcus, Pete));
        Assert.Equal("Marcus", s.HeaderSeenBy(Pete, Marcus));
        Assert.Contains(s.PanelsTo(Marcus), p => p.GetProperty("op").GetString() == "clear");
        Assert.Contains("You and Ri'Zahra know each other now.", s.Chat.TextsTo(Marcus));
        Assert.Contains("You and Marcus know each other now.", s.Chat.TextsTo(Pete));
        Assert.False(s.Social.MakeOffer(Pete, Marcus, Social.Introduce).Ok); // already known
    }

    [Fact]
    public void DecliningOrNoAnswerChangesNothing()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Introduce);
        Assert.True(s.Social.DeclineNewest(Marcus).Ok);
        Assert.Contains(s.PanelsTo(Pete), p => p.GetProperty("text").GetString() == "A broad Imperial said no.");

        s.Social.MakeOffer(Pete, Dravyn, Social.Introduce);
        s.Now += TimeSpan.FromSeconds(Social.OfferSeconds + 1);
        Assert.False(s.Social.AcceptNewest(Dravyn).Ok);

        Assert.Equal(Visibility.Public, s.Social.LevelOf(Marcus, s.Account(Pete)));
        Assert.Equal(Visibility.Public, s.Social.LevelOf(Dravyn, s.Account(Pete)));
        Assert.Contains(s.PanelsTo(Pete), p => p.GetProperty("text").GetString() == "No answer to your offer.");
    }

    [Fact]
    public void TwoOffersToEachOtherAreAYes()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Introduce);

        Assert.True(s.Social.MakeOffer(Marcus, Pete, Social.Introduce).Ok);

        Assert.Equal(Visibility.Known, s.Social.LevelOf(Pete, s.Account(Marcus)));
        Assert.False(s.Social.MakeOffer(Dravyn, Dravyn, Social.Introduce).Ok);
    }

    [Fact]
    public void FriendsSeeFriendsFieldsAndCanUndoIt()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Friend);
        s.Social.AcceptNewest(Marcus);

        Assert.Equal(Visibility.Friends, s.Social.LevelOf(Marcus, s.Account(Pete)));
        string card = PageJson.Serialize(s.Social.View(Marcus, Pete.RawValue.ToString()));
        Assert.Contains("Born in Rimmen.", card); // History is a Friends field

        Assert.True(s.Social.Unfriend(Marcus, Pete).Ok);
        Assert.Equal(Visibility.Known, s.Social.LevelOf(Marcus, s.Account(Pete))); // the introduction stays
        Assert.True(s.Social.Forget(Pete, Marcus).Ok);
        Assert.Equal(Visibility.Public, s.Social.LevelOf(Marcus, s.Account(Pete)));
        Assert.DoesNotContain("Born in Rimmen.", PageJson.Serialize(s.Social.View(Marcus, Pete.RawValue.ToString())));
    }

    [Fact]
    public void ABlockEndsEverythingAndStopsOffers()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Friend);
        s.Social.AcceptNewest(Marcus);

        Assert.True(s.Social.Block(Pete, Marcus).Ok);

        Assert.Equal(Visibility.Public, s.Social.LevelOf(Marcus, s.Account(Pete)));
        Assert.Equal(Visibility.Public, s.Social.LevelOf(Pete, s.Account(Marcus)));
        Assert.False(s.Social.MakeOffer(Marcus, Pete, Social.Introduce).Ok);
        Assert.Equal("blocked", s.Nearby(Marcus).GetProperty("players")[0].GetProperty("relation").GetString());
        Assert.True(s.Social.Unblock(Pete, Marcus).Ok);
        Assert.True(s.Social.MakeOffer(Marcus, Pete, Social.Introduce).Ok);
    }

    [Fact]
    public void RelationsSurviveARestart()
    {
        Setup s = new();
        s.Social.MakeOffer(Pete, Marcus, Social.Friend);
        s.Social.AcceptNewest(Marcus);

        s.Relations = s.NewRelations(); // a new server: everything read back from the files
        s.Social = s.NewSocial();

        Assert.Equal(Visibility.Friends, s.Social.LevelOf(Pete, s.Account(Marcus)));
        Assert.Equal(Visibility.Friends, s.Social.LevelOf(Marcus, s.Account(Pete)));
    }

    [Fact]
    public void TheCardPageRunsTheSameOffers()
    {
        Setup s = new();
        PageDesk desk = new(s.Players, s.Cards, s.Catalogs, new GameRace(null!, NullLogger.Instance), null!, s.Social, s.Config, NullLogger.Instance);
        JsonElement Ask(PlayerId who, object request) => JsonDocument.Parse(desk.Handle(who, JsonSerializer.Serialize(request))).RootElement;

        Assert.Equal(2, Ask(Pete, new { op = "nearby" }).GetProperty("players").GetArrayLength());
        Assert.True(Ask(Pete, new { op = "introduce", player = Marcus.RawValue.ToString() }).GetProperty("ok").GetBoolean());
        Assert.True(Ask(Marcus, new { op = "accept", player = Pete.RawValue.ToString() }).GetProperty("ok").GetBoolean());
        JsonElement view = Ask(Marcus, new { op = "view", player = Pete.RawValue.ToString() });
        Assert.Equal("Ri'Zahra", view.GetProperty("who").GetString());
        Assert.Equal("known", view.GetProperty("relation").GetString());
        Assert.False(Ask(Pete, new { op = "view", player = "999" }).GetProperty("ok").GetBoolean());
    }
}
