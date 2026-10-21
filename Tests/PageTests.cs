using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MESOL.Common;
using MESOL.Server;
using MESOL.Server.Cards;
using MESOL.Server.Catalog;
using MESOL.Server.Game;
using MESOL.Server.Page;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using ReadyM.Api.Idents;
using Xunit;

namespace MESOL.Tests;

public class ChunkTests
{
    [Fact]
    public void ATextComesBackWholeFromItsPieces()
    {
        string text = string.Concat(Enumerable.Repeat("Zażółć gęślą jaźń, Ri'Zahra 🐾 ", 120));
        List<string> pieces = Chunks.Split(text);
        ChunkAssembler<int> assembler = new();

        string? whole = null;
        for (int i = 0; i < pieces.Count; i++)
        {
            Assert.True(pieces[i].Length <= Chunks.Size);
            Assert.False(char.IsHighSurrogate(pieces[i][^1]), "a piece ends inside a surrogate pair");
            whole = assembler.Add(7, 1, i, pieces.Count, pieces[i]);
            Assert.True(i == pieces.Count - 1 || whole is null);
        }

        Assert.Equal(text, whole);
        Assert.Equal(0, assembler.PendingCount);
    }

    [Fact]
    public void TwoRequestsInFlightDoNotMix()
    {
        ChunkAssembler<int> assembler = new();

        Assert.Null(assembler.Add(1, 10, 0, 2, "ab"));
        Assert.Null(assembler.Add(1, 11, 0, 2, "xy"));
        Assert.Equal("abcd", assembler.Add(1, 10, 1, 2, "cd"));
        Assert.Equal("xyzw", assembler.Add(1, 11, 1, 2, "zw"));
    }

    [Fact]
    public void BadPiecesAreIgnoredAndOldOnesDropped()
    {
        ChunkAssembler<int> assembler = new(maxPending: 2);

        Assert.Null(assembler.Add(1, 1, 3, 2, "x"));
        Assert.Null(assembler.Add(1, 1, 0, Chunks.MaxCount + 1, "x"));
        Assert.Null(assembler.Add(1, 1, 0, 1, new string('x', Chunks.Size + 1)));
        Assert.Equal("", assembler.Add(1, 1, 0, 1, ""));

        assembler.Add(1, 1, 0, 2, "a");
        assembler.Add(1, 2, 0, 2, "b");
        assembler.Add(1, 3, 0, 2, "c");
        Assert.Equal(2, assembler.PendingCount);
        Assert.Null(assembler.Add(1, 1, 1, 2, "z")); // request 1 was dropped, so this starts it again
    }
}

public class PageTests
{
    private static readonly PlayerId Pete = Fakes.Player(1);

    internal sealed class Setup
    {
        public readonly FakePlayers Players = new();
        public readonly CardStore Cards = StoreTests.Store(Fakes.TempFolder());
        public readonly MesolConfig Config = new();
        public readonly PageDesk Desk;
        public readonly Queue<int[]> Dice = new();
        public readonly List<string> Rolled = new();

        public Setup()
        {
            Players.Accounts[Pete] = Guid.NewGuid();
            MesolData data = new(new Mod(NullLogger.Instance), Config);
            CatalogService catalogs = new(data, NullLogger.Instance);
            catalogs.Use(Fakes.SmallCatalog());
            Desk = new PageDesk(Players, Cards, catalogs, new GameRace(null!, NullLogger.Instance), null!, null!, Config, NullLogger.Instance)
            {
                Clock = () => Fakes.Now,
                RaceOf = _ => "race.khajiit",
                RollDice = (_, label) =>
                {
                    Rolled.Add(label);
                    return Dice.Dequeue();
                },
            };
        }

        public JsonElement Ask(object request) => JsonDocument.Parse(Desk.Handle(Pete, JsonSerializer.Serialize(request))).RootElement;
    }

    private static JsonElement Field(JsonElement answer, string id) => answer.GetProperty("card").GetProperty(id);

    [Fact]
    public void LoadGivesFieldsTheEmptyCardAndTheGamesRace()
    {
        Setup s = new();

        JsonElement a = s.Ask(new { op = "load" });

        Assert.True(a.GetProperty("ok").GetBoolean());
        Assert.Equal("canon", a.GetProperty("mode").GetString());
        Assert.Equal(CardFields.All.Count, a.GetProperty("fields").GetArrayLength());
        // every field type reaches the page under its own name (a trait sent as "text" would be typed, not rolled)
        string[] types = a.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("type").GetString()!).ToArray();
        Assert.Equal(24, types.Count(t => t == "trait"));
        Assert.Equal(new[] { "catalog", "choice", "longText", "text", "trait" }, types.Distinct().OrderBy(t => t, StringComparer.Ordinal));
        Assert.True(Field(a, "firstName").GetProperty("empty").GetBoolean());
        Assert.Equal("known", Field(a, "firstName").GetProperty("vis").GetString());
        Assert.Equal("Khajiit", a.GetProperty("raceHint").GetProperty("name").GetString());
    }

    [Fact]
    public void RaceAndPlacesArePickedNeverTyped()
    {
        Setup s = new();

        JsonElement typed = s.Ask(new { op = "set", field = "race", text = "Dupa" });
        Assert.False(typed.GetProperty("ok").GetBoolean());
        Assert.Contains("picked from the lore list", typed.GetProperty("message").GetString());
        Assert.True(Field(typed, "race").GetProperty("empty").GetBoolean());

        JsonElement races = s.Ask(new { op = "options", field = "race" });
        Assert.Equal(new[] { "Khajiit", "Nord" }, races.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ToArray());

        JsonElement picked = s.Ask(new { op = "pick", field = "race", id = "race.khajiit" });
        Assert.True(picked.GetProperty("ok").GetBoolean());
        Assert.Equal("Khajiit", Field(picked, "race").GetProperty("shown").GetString());
    }

    [Fact]
    public void PickersOfferOnlyWhatFitsTheCard()
    {
        Setup s = new();

        JsonElement noRace = s.Ask(new { op = "options", field = "subtype" });
        Assert.Equal(0, noRace.GetProperty("options").GetArrayLength());
        Assert.Equal("Pick your race first.", noRace.GetProperty("message").GetString());

        s.Ask(new { op = "pick", field = "race", id = "race.khajiit" });
        string[] kinds = s.Ask(new { op = "options", field = "subtype" }).GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "Cathay", "Cathay-raht" }, kinds); // no Senche (implausible), nothing Nord

        string[] factions = s.Ask(new { op = "options", field = "affiliation" }).GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "Fighters Guild", "Mages Guild" }, factions); // no historical, no future

        Assert.Equal(0, s.Ask(new { op = "options", field = "rank" }).GetProperty("options").GetArrayLength());
        s.Ask(new { op = "pick", field = "affiliation", id = "faction.mages-guild" });
        JsonElement ranks = s.Ask(new { op = "options", field = "rank" });
        Assert.Equal("Apprentice", Assert.Single(ranks.GetProperty("options").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public void TextChoicesVisibilityAndClear()
    {
        Setup s = new();

        JsonElement set = s.Ask(new { op = "set", field = "marks", text = "blizna nad okiem, ślad po oparzeniu" });
        Assert.Equal("blizna nad okiem, ślad po oparzeniu", Field(set, "marks").GetProperty("text").GetString());

        JsonElement history = s.Ask(new { op = "set", field = "history", text = "Born in Rimmen.\nRaised in Bravil." });
        Assert.Equal("Born in Rimmen.\nRaised in Bravil.", Field(history, "history").GetProperty("text").GetString());

        Assert.True(s.Ask(new { op = "set", field = "consent.death", text = "ask" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "set", field = "consent.death", text = "maybe" }).GetProperty("ok").GetBoolean());

        Assert.Equal("public", Field(s.Ask(new { op = "vis", field = "history", vis = "public" }), "history").GetProperty("vis").GetString());
        Assert.False(s.Ask(new { op = "vis", field = "seenAs", vis = "private" }).GetProperty("ok").GetBoolean());

        JsonElement cleared = s.Ask(new { op = "clear", field = "history" });
        Assert.True(Field(cleared, "history").GetProperty("empty").GetBoolean());
        Assert.Equal("public", Field(cleared, "history").GetProperty("vis").GetString());
    }

    [Fact]
    public void PreviewShowsOnlyWhatThatViewerMaySee()
    {
        Setup s = new();
        s.Ask(new { op = "set", field = "firstName", text = "Ri'Zahra" });
        s.Ask(new { op = "set", field = "seenAs", text = "a scarred Khajiit" });
        s.Ask(new { op = "set", field = "notes", text = "Owes Marcus 40 septims." });
        s.Ask(new { op = "set", field = "currently", text = "Haggling." });

        JsonElement stranger = s.Ask(new { op = "preview", level = "public" }).GetProperty("view");
        string all = stranger.GetRawText();
        Assert.Equal("a scarred Khajiit", stranger.GetProperty("header").GetString());
        Assert.Contains("Haggling.", all);
        Assert.DoesNotContain("Ri'Zahra", all);
        Assert.DoesNotContain("septims", all);

        JsonElement known = s.Ask(new { op = "preview", level = "known" }).GetProperty("view");
        Assert.Equal("Ri'Zahra", known.GetProperty("header").GetString());
        Assert.DoesNotContain("septims", known.GetRawText());

        Assert.False(s.Ask(new { op = "preview", level = "private" }).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void TypedTextInCatalogFieldsGoesOnACanonServer()
    {
        Setup s = new();
        s.Config.CatalogMode = "canonPlus";
        s.Ask(new { op = "set", field = "race", text = "Dupa" });
        s.Ask(new { op = "set", field = "firstName", text = "Ri'Zahra" });
        s.Config.CatalogMode = "canon";

        JsonElement a = s.Ask(new { op = "load" });

        Assert.True(Field(a, "race").GetProperty("empty").GetBoolean());
        Assert.Equal("Ri'Zahra", Field(a, "firstName").GetProperty("text").GetString());
    }

    [Fact]
    public void BadRequestsGetAnAnswerNotAnException()
    {
        Setup s = new();

        Assert.False(JsonDocument.Parse(s.Desk.Handle(Pete, "not json")).RootElement.GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "frobnicate", field = "title" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "set", field = "nmae", text = "x" }).GetProperty("ok").GetBoolean());
        s.Players.Accounts.Clear();
        Assert.Contains("ReadyM account", s.Ask(new { op = "load" }).GetProperty("message").GetString());
    }

    [Fact]
    public void TheRateLimitHoldsPerPlayer()
    {
        DateTimeOffset now = Fakes.Now;
        PageLink link = new() { Clock = () => now };

        for (int i = 0; i < PageLink.RateLimit; i++)
        {
            Assert.True(link.Allow(Pete));
        }
        Assert.False(link.Allow(Pete));
        Assert.True(link.Allow(Fakes.Player(2)));
        now += PageLink.RateWindow + TimeSpan.FromSeconds(1);
        Assert.True(link.Allow(Pete));
    }

    /// <summary>
    /// The browser preview's stand-in server builds its fields from web-src/src/preview-fields.json. Set
    /// MESOL_UPDATE_PREVIEW=1 to rewrite that file after changing CardFields.
    /// </summary>
    [Fact]
    public void ThePreviewKnowsTheSameFields()
    {
        string json = JsonSerializer.Serialize(PageJson.Fields(), new JsonSerializerOptions(PageJson.Options) { WriteIndented = true })
            .ReplaceLineEndings("\n") + "\n";
        string path = Path.Combine(Fakes.RepoMesol, "web-src", "src", "preview-fields.json");
        if (Environment.GetEnvironmentVariable("MESOL_UPDATE_PREVIEW") == "1")
        {
            File.WriteAllText(path, json);
        }
        Assert.True(File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == json,
            "web-src/src/preview-fields.json is out of date: run the tests once with MESOL_UPDATE_PREVIEW=1.");
    }
}
