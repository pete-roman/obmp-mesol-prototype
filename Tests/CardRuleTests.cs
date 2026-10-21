using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using Xunit;

namespace MESOL.Tests;

public class VisibilityTests
{
    [Theory]
    [InlineData(true, false, false, Visibility.Private)]
    [InlineData(false, true, true, Visibility.Friends)]
    [InlineData(false, false, true, Visibility.Known)]
    [InlineData(false, false, false, Visibility.Public)]
    public void ViewerLevelIsTheHighestThatApplies(bool owner, bool friend, bool known, Visibility expected)
    {
        Assert.Equal(expected, ViewerLevel.Of(owner, friend, known));
    }

    [Fact]
    public void EachLevelSeesItsTierAndTheOnesBelow()
    {
        CardFile card = CardWith(("firstName", "Ri'Zahra", Visibility.Known), ("history", "Born under two moons.", Visibility.Friends),
            ("notes", "Owes Marcus 40 septims.", Visibility.Private), ("currently", "Haggling.", Visibility.Public));

        Assert.Equal(new[] { "currently" }, Ids(card, Visibility.Public));
        Assert.Equal(new[] { "firstName", "currently" }, Ids(card, Visibility.Known));
        Assert.Equal(new[] { "firstName", "history", "currently" }, Ids(card, Visibility.Friends));
        Assert.Equal(new[] { "firstName", "history", "notes", "currently" }, Ids(card, Visibility.Private));
    }

    [Fact]
    public void LockedFieldsKeepTheirTierWhateverTheFileSays()
    {
        // A hand-edited file cannot make private notes public, or hide the consent flags.
        CardFile card = CardWith(("notes", "secret", Visibility.Public), ("consent.death", "ask", Visibility.Private));

        Assert.Equal(new[] { "consent.death" }, Ids(card, Visibility.Public));
    }

    [Fact]
    public void EmptyFieldsAreNeverShown()
    {
        CardFile card = CardWith(("title", "", Visibility.Public));

        Assert.Empty(Ids(card, Visibility.Private));
        Assert.True(card.IsEmpty);
    }

    [Fact]
    public void LockedFieldsRefuseANewTier()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        EditResult result = Editor().SetVisibility(card, Field("seenAs"), Visibility.Private);

        Assert.False(result.Ok);
        Assert.Contains("always Public", result.Message);
    }

    internal static CardFile CardWith(params (string Id, string Text, Visibility Visibility)[] fields)
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        foreach ((string id, string text, Visibility visibility) in fields)
        {
            card.Put(Field(id), new FieldValue { Text = text, Visibility = visibility }, Fakes.Now);
        }
        return card;
    }

    internal static FieldDefinition Field(string id)
    {
        Assert.True(CardFields.TryGet(id, out FieldDefinition field), id);
        return field;
    }

    internal static CardEditor Editor(CatalogMode mode = CatalogMode.CanonPlus, bool implausible = false) =>
        new(Fakes.SmallCatalog(), mode, implausible, () => Fakes.Now);

    private static string[] Ids(CardFile card, Visibility level) => CardView.Visible(card, level).Select(v => v.Field.Id).ToArray();
}

public class EditorTests
{
    private static FieldDefinition Field(string id) => VisibilityTests.Field(id);

    private static CardEditor Editor(CatalogMode mode = CatalogMode.CanonPlus, bool implausible = false) => VisibilityTests.Editor(mode, implausible);

    [Fact]
    public void SetKeepsTheDefaultTierAndBumpsThePartVersion()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        EditResult result = Editor().Set(card, Field("firstName"), "  Ri'Zahra  ");

        Assert.True(result.Ok, result.Message);
        Assert.Equal("Ri'Zahra", card.Get(Field("firstName"))!.Text);
        Assert.Equal(Visibility.Known, card.Get(Field("firstName"))!.Visibility);
        Assert.Equal(1, card.VersionOf(CardPart.Identity));
        Assert.Equal(0, card.VersionOf(CardPart.Story));
    }

    [Fact]
    public void TooLongTextIsRefusedWithTheLimit()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        EditResult result = Editor().Set(card, Field("firstName"), new string('a', 31));

        Assert.False(result.Ok);
        Assert.Contains("at most 30", result.Message);
        Assert.True(card.IsEmpty);
    }

    [Fact]
    public void ChoicesTakeOnlyTheirWords()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        Assert.False(Editor().Set(card, Field("consent.death"), "maybe").Ok);
        Assert.True(Editor().Set(card, Field("consent.death"), "ASK").Ok);
        Assert.Equal("ask", card.Get(Field("consent.death"))!.Text);
    }

    [Fact]
    public void CanonModeRefusesFreeTextOnCatalogFields()
    {
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        Assert.False(Editor(CatalogMode.Canon).Set(card, Field("homeland"), "Rimmen").Ok);
        Assert.True(Editor(CatalogMode.Canon).Set(card, Field("firstName"), "Ri'Zahra").Ok);
    }

    [Fact]
    public void CanonPlusMarksFreeTextAsCustomButNotAnAdditionToAPick()
    {
        CardEditor editor = Editor();
        LoreCatalog catalog = Fakes.SmallCatalog();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        editor.Set(card, Field("faith"), "The Hollow Moon");
        Assert.True(card.Get(Field("faith"))!.NonCanon);

        Assert.True(catalog.TryGet("race.khajiit", out CatalogEntry khajiit));
        editor.Pick(card, Field("race"), khajiit);
        Assert.True(catalog.TryGet("province.elsweyr", out CatalogEntry elsweyr));
        editor.Pick(card, Field("homeland"), elsweyr);
        editor.Set(card, Field("homeland"), "Rimmen outskirts");
        FieldValue homeland = card.Get(Field("homeland"))!;
        Assert.Equal("province.elsweyr", homeland.CatalogId);
        Assert.Equal("Rimmen outskirts", homeland.Text);
        Assert.False(homeland.NonCanon);
    }

    [Fact]
    public void FutureEntriesAreNeverOfferedAndHistoricalOnlyForHeritage()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        List<CatalogEntry> factions = editor.Candidates(card, Field("affiliation"), "", out _);
        Assert.Equal(new[] { "Fighters Guild", "Mages Guild" }, factions.Select(e => e.Name).ToArray());

        Pick(editor, card, "race", "race.nord");
        List<CatalogEntry> kinds = editor.Candidates(card, Field("subtype"), "", out string? problem);
        Assert.Null(problem);
        Assert.Equal(new[] { "Atmoran descent" }, kinds.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SubtypesFollowTheRaceAndTheModelFit()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        editor.Candidates(card, Field("subtype"), "", out string? problem);
        Assert.Contains("race first", problem);

        Pick(editor, card, "race", "race.khajiit");
        Assert.Equal(new[] { "Cathay", "Cathay-raht" }, editor.Candidates(card, Field("subtype"), "cathay", out _).Select(e => e.Name).ToArray());
        Assert.Empty(editor.Candidates(card, Field("subtype"), "senche", out _));
        Assert.Single(Editor(implausible: true).Candidates(card, Field("subtype"), "senche", out _));
    }

    [Fact]
    public void ANewRaceClearsTheOldRacesSubtype()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        Pick(editor, card, "race", "race.khajiit");
        Pick(editor, card, "subtype", "khajiit.furstock.cathay-raht");

        EditResult result = Pick(editor, card, "race", "race.nord");

        Assert.True(result.Ok);
        Assert.Contains("Kind cleared", result.Message);
        Assert.True(card.Get(Field("subtype"))!.IsEmpty);
    }

    [Fact]
    public void RanksBelongToTheChosenAffiliation()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        Pick(editor, card, "affiliation", "faction.fighters-guild");

        Assert.Equal(new[] { "Associate" }, editor.Candidates(card, Field("rank"), "", out _).Select(e => e.Name).ToArray());
        Assert.False(Pick(editor, card, "rank", "rank.mages-guild.apprentice").Ok);
    }

    [Fact]
    public void MixedAncestryEntriesAreNotPickedAsKinds()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        Pick(editor, card, "race", "race.nord");

        Assert.DoesNotContain(editor.Candidates(card, Field("subtype"), "", out _), e => e.Name == "Cyro-Nord");
    }

    [Fact]
    public void LongFieldsGrowWithAddUpToTheirLimit()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);

        Assert.True(editor.Append(card, Field("history"), "Born in Rimmen.").Ok);
        Assert.True(editor.Append(card, Field("history"), "Left at sixteen.").Ok);
        Assert.Equal("Born in Rimmen. Left at sixteen.", card.Get(Field("history"))!.Text);

        Assert.False(editor.Append(card, Field("history"), new string('x', 2000)).Ok);
        Assert.False(editor.Append(card, Field("title"), "more").Ok);
    }

    [Fact]
    public void ClearKeepsTheTierForNextTime()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        editor.Set(card, Field("title"), "Caravan guard");
        editor.SetVisibility(card, Field("title"), Visibility.Friends);

        Assert.True(editor.Clear(card, Field("title")).Ok);
        editor.Set(card, Field("title"), "Sword of the road");

        Assert.Equal(Visibility.Friends, card.Get(Field("title"))!.Visibility);
    }

    [Fact]
    public void TheWholeCardHasACap()
    {
        CardEditor editor = Editor();
        CardFile card = CardFile.New(Guid.NewGuid(), Fakes.Now);
        foreach (string id in new[] { "physical", "personality", "history", "notes" })
        {
            Assert.True(editor.Set(card, Field(id), new string('x', 2000)).Ok);
        }
        Assert.Equal(8000, card.TextLength);
        Assert.True(CardFields.MaxCardCharacters > card.TextLength);
    }

    [Fact]
    public void CleanDropsControlCharactersAndExtraSpaces()
    {
        Assert.Equal("a b c", CardEditor.Clean("a\t b\u0007  \r\nc ", multiline: false));
    }

    private static EditResult Pick(CardEditor editor, CardFile card, string field, string id)
    {
        Assert.True(Fakes.SmallCatalog().TryGet(id, out CatalogEntry entry), id);
        return editor.Pick(card, Field(field), entry);
    }
}

public class CatalogTests
{
    [Fact]
    public void TheShippedCatalogLoadsWithoutWarnings()
    {
        List<string> warnings = new();

        LoreCatalog catalog = LoreCatalog.Load(File.ReadAllText(Fakes.ShippedCatalogPath), Array.Empty<(string, string)>(), warnings);

        Assert.Empty(warnings);
        Assert.True(catalog.Count > 700);
        foreach (string race in new[] { "khajiit", "argonian", "bosmer", "altmer", "dunmer", "orsimer", "redguard", "imperial", "breton", "nord" })
        {
            Assert.True(catalog.TryGet("race." + race, out _), race);
        }
        Assert.Equal(20, catalog.Entries.Count(e => e.RemasteredOrigin));
        Assert.All(catalog.Entries.Where(e => e.Parent is not null), e => Assert.True(catalog.TryGet(e.Parent, out _), e.Id));
    }

    [Fact]
    public void EveryCatalogFieldHasSomethingToPickInTheShippedCatalog()
    {
        LoreCatalog catalog = LoreCatalog.Load(File.ReadAllText(Fakes.ShippedCatalogPath), Array.Empty<(string, string)>(), new List<string>());
        foreach (FieldDefinition field in CardFields.All.Where(f => f.Type == FieldType.Catalog))
        {
            Assert.True(catalog.Entries.Any(e => field.CatalogKinds!.Contains(e.Kind)), field.Id);
        }
    }

    [Fact]
    public void OwnerFilesAddReplaceAndHide()
    {
        List<string> warnings = new();
        (string, string)[] additions =
        {
            ("10-mine.json", """[ { "id": "faction.blackwood-company", "kind": "faction", "name": "Blackwood Company", "era": "current" } ]"""),
            ("20-hide.json", """{ "hide": [ "faction.mages-guild", "faction.nope" ], "entries": [ { "id": "birthsign.the-thief", "kind": "birthsign", "name": "The Thief (renamed)", "era": "current" } ] }"""),
        };

        LoreCatalog catalog = LoreCatalog.Load(Fakes.SmallCatalogJson, additions, warnings);

        Assert.True(catalog.TryGet("faction.blackwood-company", out _));
        Assert.False(catalog.TryGet("faction.mages-guild", out _));
        Assert.Equal("The Thief (renamed)", catalog.NameOf("birthsign.the-thief"));
        Assert.Single(warnings, w => w.Contains("faction.nope"));
    }

    [Fact]
    public void BrokenFilesSayWhichFile()
    {
        FormatException error = Assert.Throws<FormatException>(() =>
            LoreCatalog.Load(Fakes.SmallCatalogJson, new[] { ("bad.json", "{ nope") }, new List<string>()));

        Assert.StartsWith("bad.json", error.Message);
    }

    [Fact]
    public void EntriesWithAnUnknownEraAreSkipped()
    {
        List<string> warnings = new();

        LoreCatalog catalog = LoreCatalog.Load("""[ { "id": "x", "kind": "faction", "name": "X", "era": "someday" } ]""", Array.Empty<(string, string)>(), warnings);

        Assert.Equal(0, catalog.Count);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("/Game/Forms/races/Khajiit.Khajiit", "race.khajiit")]
    [InlineData("/Game/Forms/Races/DarkElfRace", "race.dunmer")]
    [InlineData("/Game/Forms/Races/HighElf", "race.altmer")]
    [InlineData("/Game/Forms/Races/WoodElf", "race.bosmer")]
    [InlineData("/Game/Forms/Races/Orc", "race.orsimer")]
    [InlineData("/Game/Forms/Races/Nord", "race.nord")]
    [InlineData("", null)]
    [InlineData("/Game/Forms/Races/Dremora", null)]
    public void RacePathsMapToCatalogRaces(string path, string? expected)
    {
        Assert.Equal(expected, GameRace.Map(path));
    }
}
