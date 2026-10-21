using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MESOL.Server;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Commands;
using MESOL.Server.Game;
using MESOL.Server.Page;
using MESOL.Server.Relations;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using Xunit;

namespace MESOL.Tests;

public class StoreTests
{
    [Fact]
    public void ACardSurvivesSaveAndLoad()
    {
        string folder = Fakes.TempFolder();
        Guid owner = Guid.NewGuid();
        CardFile card = VisibilityTests.CardWith(("firstName", "Ri'Zahra", Visibility.Known));
        card.Owner = owner;

        Store(folder).Save(card);
        CardFile? loaded = Store(folder).Find(owner);

        Assert.NotNull(loaded);
        Assert.Equal("Ri'Zahra", loaded!.Get(VisibilityTests.Field("firstName"))!.Text);
        Assert.Equal(Visibility.Known, loaded.Get(VisibilityTests.Field("firstName"))!.Visibility);
        Assert.Equal(1, loaded.VersionOf(CardPart.Identity));
        Assert.False(File.Exists(Path.Combine(folder, owner.ToString("D") + ".json.tmp")));
    }

    [Fact]
    public void ASecondSaveKeepsThePreviousFileAsBak()
    {
        string folder = Fakes.TempFolder();
        CardFile card = VisibilityTests.CardWith(("title", "One", Visibility.Public));
        CardStore store = Store(folder);

        store.Save(card);
        card.Put(VisibilityTests.Field("title"), new FieldValue { Text = "Two" }, Fakes.Now);
        store.Save(card);

        string path = Path.Combine(folder, card.Owner.ToString("D") + ".json");
        Assert.Contains("Two", File.ReadAllText(path));
        Assert.Contains("One", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void AnUnreadableFileIsNeverOverwritten()
    {
        string folder = Fakes.TempFolder();
        Guid owner = Guid.NewGuid();
        string path = Path.Combine(folder, owner.ToString("D") + ".json");
        File.WriteAllText(path, "{ broken");
        CardStore store = Store(folder);

        CardFile card = store.GetOrCreate(owner);
        card.Put(VisibilityTests.Field("title"), new FieldValue { Text = "New" }, Fakes.Now);
        store.Save(card);

        Assert.False(store.CanSave(owner));
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public void TheWriterThreadWritesTheNewestContent()
    {
        string folder = Fakes.TempFolder();
        string path = Path.Combine(folder, "a.json");
        List<Exception> errors = new();
        using (FileWriter writer = new((_, e) => errors.Add(e)))
        {
            for (int i = 0; i < 50; i++)
            {
                writer.Write(path, i.ToString());
            }
        }
        Assert.Empty(errors);
        Assert.Equal("49", File.ReadAllText(path));
    }

    [Fact]
    public void TheDataFolderIsOutsideTheModFolder()
    {
        string modDirectory = Path.Combine("C:", "srv", "mods", "MESOL", "server");

        Assert.Equal(Path.GetFullPath(Path.Combine("C:", "srv", "moddata", "MESOL")), MesolData.Resolve(modDirectory, ""));
        Assert.Equal(Path.GetFullPath(Path.Combine("C:", "srv", "lore")), MesolData.Resolve(modDirectory, "lore"));
    }

    internal static CardStore Store(string folder) =>
        new(new MesolData(new Mod(NullLogger.Instance), new MesolConfig()), NullLogger.Instance) { FolderOverride = folder };
}

public class CardTextTests
{
    private static CardText Text() => new(Fakes.SmallCatalog(), markCustom: true);

    [Fact]
    public void StrangersSeeTheSeenAsLineNotTheName()
    {
        CardFile card = VisibilityTests.CardWith(("firstName", "Ri'Zahra", Visibility.Known),
            ("seenAs", "a scarred Khajiit with a merchant's sash", Visibility.Public), ("icOoc", "ic", Visibility.Public));

        Assert.Equal("a scarred Khajiit with a merchant's sash (in character)", Text().Header(card, Visibility.Public));
        Assert.Equal("Ri'Zahra (in character)", Text().Header(card, Visibility.Known));
    }

    [Fact]
    public void WithoutSeenAsStrangersSeeTheRaceOrNothing()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        Assert.Equal("someone you don't know", Text().Header(card, Visibility.Public));

        card.Put(VisibilityTests.Field("race"), new FieldValue { CatalogId = "race.khajiit" }, Fakes.Now);
        Assert.Equal("a Khajiit", Text().Header(card, Visibility.Public));
    }

    [Fact]
    public void LinesStayUnderChatsLimitAndLongTextsArePreviewed()
    {
        CardFile card = VisibilityTests.CardWith(("history", string.Join(" ", Enumerable.Repeat("word", 400)), Visibility.Public),
            ("glance", string.Join(" ", Enumerable.Repeat("glance", 45)), Visibility.Public),
            ("currently", "Haggling with the smith.", Visibility.Public));

        List<string> lines = Text().Lines(card, Visibility.Public, showVisibility: false, moreHint: "more: /lore 3 <field>");

        Assert.All(lines, line => Assert.True(line.Length <= 240, line));
        Assert.Contains(lines, line => line.Contains("History: word word") && line.Contains("..."));
        Assert.Equal("more: /lore 3 <field>", lines[^1]);
    }

    [Fact]
    public void CustomTextIsMarkedAndAdditionsFollowThePick()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        card.Put(VisibilityTests.Field("faith"), new FieldValue { Text = "The Hollow Moon", NonCanon = true }, Fakes.Now);
        card.Put(VisibilityTests.Field("homeland"), new FieldValue { CatalogId = "khajiit.region.anequina", Text = "Rimmen outskirts" }, Fakes.Now);

        string all = string.Join("\n", Text().Lines(card, Visibility.Private, showVisibility: false, moreHint: ""));

        Assert.Contains("Faith: The Hollow Moon (custom)", all);
        Assert.Contains("Homeland: Anequina (Rimmen outskirts)", all);
    }
}

public class CommandTests
{
    private static readonly PlayerId Pete = Fakes.Player(1);
    private static readonly PlayerId Marcus = Fakes.Player(2);

    private sealed class Setup
    {
        public readonly FakeChat Chat = new();
        public readonly FakePlayers Players = new();
        public readonly CardStore Cards = StoreTests.Store(Fakes.TempFolder());
        public readonly MesolConfig Config = new();
        public readonly PageLink Page = new();
        public RelationStore Relations = null!;
        public Social Social = null!;
        public readonly MyLoreCommand MyLore;
        public readonly LoreCommand Lore;
        public PlayerId? Target;

        public Setup()
        {
            Chat.Players.Add(new ChatPlayer(Pete, "Pete"));
            Chat.Players.Add(new ChatPlayer(Marcus, "Marcus"));
            Players.Accounts[Pete] = Guid.NewGuid();
            Players.Accounts[Marcus] = Guid.NewGuid();
            MesolData data = new(new Mod(NullLogger.Instance), Config);
            CatalogService catalogs = new(data, NullLogger.Instance);
            catalogs.Use(Fakes.SmallCatalog());
            MyLore = new MyLoreCommand(Chat, Players, Cards, catalogs, new GameRace(null!, NullLogger.Instance), Page, Config, NullLogger.Instance)
            {
                Clock = () => Fakes.Now,
                RaceOf = _ => "race.khajiit",
            };
            Relations = new RelationStore(data, NullLogger.Instance) { FolderOverride = Fakes.TempFolder() };
            Social = new Social(Players, Chat, Cards, Relations, catalogs, new Targets(Chat, null!), Page, Config, NullLogger.Instance);
            Lore = new LoreCommand(Chat, Players, Cards, catalogs, new Targets(Chat, null!), Page, Social, Config, NullLogger.Instance)
            {
                FindTarget = Find,
            };
        }

        private bool Find(PlayerId sender, string who, float range, out PlayerId target, out string problem)
        {
            problem = "nobody near";
            target = Target ?? default;
            return Target is not null;
        }

        public List<string> Run(PlayerId player, string arguments)
        {
            Chat.Sent.Clear();
            MyLore.Run(Chat, player, arguments);
            return Chat.TextsTo(player);
        }

        public List<string> Look(PlayerId viewer, PlayerId? at, string arguments = "")
        {
            Chat.Sent.Clear();
            Target = at;
            Lore.Run(Chat, viewer, arguments);
            return Chat.TextsTo(viewer);
        }
    }

    [Fact]
    public void AnEmptyCardSuggestsTheGamesRace()
    {
        Setup s = new();

        List<string> lines = s.Run(Pete, "");

        Assert.Contains(lines, l => l.StartsWith("Your card is empty"));
        Assert.Contains("Your character looks like a Khajiit in the game: /mylore pick race Khajiit", lines);
    }

    [Fact]
    public void SetPickAndVisibilityEndOnTheCard()
    {
        Setup s = new();

        Assert.Equal("First name set: Ri'Zahra [Known].", Assert.Single(s.Run(Pete, "set firstName Ri'Zahra")));
        Assert.Equal("Race set: Khajiit [Public].", Assert.Single(s.Run(Pete, "pick race khajiit")));

        List<string> offer = s.Run(Pete, "pick subtype cathay");
        Assert.Equal("Kind: 1 Cathay, 2 Cathay-raht. Then: /mylore pick subtype <number>", Assert.Single(offer));
        Assert.Equal("Kind set: Cathay-raht [Public].", Assert.Single(s.Run(Pete, "pick subtype 2")));

        Assert.Equal("History is now Private.", Assert.Single(s.Run(Pete, "vis history private")));

        string own = string.Join("\n", s.Run(Pete, ""));
        Assert.Contains("First name: Ri'Zahra [Known]", own);
        Assert.Contains("Kind: Cathay-raht [Public]", own);
    }

    [Fact]
    public void MistakesGetAHelpfulRejection()
    {
        Setup s = new();

        Assert.Contains("There is no field 'nmae'", Assert.Single(s.Run(Pete, "set nmae Bob")));
        Assert.Contains("There is no /mylore frobnicate", Assert.Single(s.Run(Pete, "frobnicate")));
        Assert.Contains("at most 30", Assert.Single(s.Run(Pete, "set firstName " + new string('a', 40))));
        Assert.Contains("public, known, friends or private", Assert.Single(s.Run(Pete, "vis title everyone")));
        Assert.Contains(s.Chat.Sent, l => l.Kind == ChatLineKind.Rejection);
    }

    [Fact]
    public void NoReadyMIdMeansNoCard()
    {
        Setup s = new();
        s.Players.Accounts.Remove(Pete);

        Assert.Contains("doesn't know your ReadyM account", Assert.Single(s.Run(Pete, "set title Guard")));
    }

    [Fact]
    public void OthersSeeOnlyThePublicPartOfACard()
    {
        Setup s = new();
        s.Run(Pete, "set firstName Ri'Zahra");
        s.Run(Pete, "set seenAs a scarred Khajiit with a merchant's sash");
        s.Run(Pete, "set currently Haggling with the smith.");
        s.Run(Pete, "set history Born in Rimmen.");
        s.Run(Pete, "vis history public");
        s.Run(Pete, "set notes Owes Marcus 40 septims.");

        string seen = string.Join("\n", s.Look(Marcus, Pete));

        Assert.StartsWith("a scarred Khajiit with a merchant's sash", seen);
        Assert.Contains("Currently: Haggling with the smith.", seen);
        Assert.Contains("History: Born in Rimmen.", seen);
        Assert.DoesNotContain("Ri'Zahra", seen);
        Assert.DoesNotContain("septims", seen);
    }

    [Fact]
    public void AskingForAHiddenFieldLooksLikeAnEmptyOne()
    {
        Setup s = new();
        s.Run(Pete, "set firstName Ri'Zahra");
        s.Run(Pete, "set currently Haggling.");

        Assert.Equal("Nothing to show for First name.", Assert.Single(s.Look(Marcus, Pete, "firstName")));
        Assert.Equal("Nothing to show for Personality.", Assert.Single(s.Look(Marcus, Pete, "personality")));
    }

    [Fact]
    public void LookingAtSomeoneWithoutACardOrWithoutATarget()
    {
        Setup s = new();

        Assert.Equal("Pete has no lore card yet.", Assert.Single(s.Look(Marcus, Pete)));
        Assert.Equal("nobody near", Assert.Single(s.Look(Marcus, null)));
    }

    [Fact]
    public void RaceLockKeepsTheGamesRace()
    {
        Setup s = new();
        s.Config.RaceLock = true;

        Assert.Contains("keeps the card's race the same", Assert.Single(s.Run(Pete, "pick race nord")));
        Assert.Equal("Race set: Khajiit [Public].", Assert.Single(s.Run(Pete, "pick race khajiit")));
    }

    [Fact]
    public void AFailingRaceLookupStillShowsTheCard()
    {
        Setup s = new();
        s.MyLore.RaceOf = _ => throw new InvalidOperationException("test");

        Assert.StartsWith("Your card is empty", Assert.Single(s.Run(Pete, "")));
    }

    [Fact]
    public void AHandlerThatThrowsAnswersAndCarriesOn()
    {
        Setup s = new();
        s.Config.RaceLock = true;
        s.MyLore.RaceOf = _ => throw new InvalidOperationException("test");

        Assert.Equal("That didn't work. Try again.", Assert.Single(s.Run(Pete, "pick race nord")));
        Assert.Equal("Title set: Guard [Known].", Assert.Single(s.Run(Pete, "set title Guard")));
    }
}
