using System.Linq;
using System.Text.Json;
using Xunit;

namespace MESOL.Tests;

/// <summary>
/// RP traits on the card page: typed once (fixed at once) or rolled with 5d20 through RpRoll, one reroll, then fixed.
/// And the background: homeland province (own lands first), a home region inside it, upbringing by race.
/// </summary>
public class TraitAndBackgroundTests
{
    private static JsonElement Field(JsonElement answer, string id) => answer.GetProperty("card").GetProperty(id);

    [Fact]
    public void ATypedTraitIsFixedAtOnce()
    {
        PageTests.Setup s = new();

        JsonElement a = s.Ask(new { op = "set", field = "trait.lazy", text = "63" });

        Assert.True(a.GetProperty("ok").GetBoolean());
        Assert.Equal(63, Field(a, "trait.lazy").GetProperty("number").GetInt32());
        Assert.True(Field(a, "trait.lazy").GetProperty("fixed").GetBoolean());
        Assert.False(s.Ask(new { op = "set", field = "trait.lazy", text = "90" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "roll", field = "trait.lazy" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "clear", field = "trait.lazy" }).GetProperty("ok").GetBoolean());
        Assert.Empty(s.Rolled);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("101")]
    [InlineData("lots")]
    public void ATypedTraitIsFiveToOneHundred(string text)
    {
        PageTests.Setup s = new();

        JsonElement a = s.Ask(new { op = "set", field = "trait.lazy", text });

        Assert.False(a.GetProperty("ok").GetBoolean());
        Assert.Contains("5 to 100", a.GetProperty("message").GetString());
    }

    [Fact]
    public void ARollAllowsOneRerollThenIsFixed()
    {
        PageTests.Setup s = new();
        s.Dice.Enqueue(new[] { 3, 4, 5, 6, 7 });
        s.Dice.Enqueue(new[] { 20, 19, 18, 17, 16 });

        JsonElement first = s.Ask(new { op = "roll", field = "trait.romantic" });
        Assert.Equal(25, Field(first, "trait.romantic").GetProperty("number").GetInt32());
        Assert.False(Field(first, "trait.romantic").GetProperty("fixed").GetBoolean());
        Assert.False(first.GetProperty("roll").GetProperty("reroll").GetBoolean());
        Assert.False(s.Ask(new { op = "set", field = "trait.romantic", text = "99" }).GetProperty("ok").GetBoolean());

        JsonElement second = s.Ask(new { op = "roll", field = "trait.romantic" });
        Assert.Equal(90, Field(second, "trait.romantic").GetProperty("number").GetInt32());
        Assert.True(Field(second, "trait.romantic").GetProperty("fixed").GetBoolean());
        Assert.Equal(2, Field(second, "trait.romantic").GetProperty("rolls").GetArrayLength());

        JsonElement third = s.Ask(new { op = "roll", field = "trait.romantic" });
        Assert.False(third.GetProperty("ok").GetBoolean());
        Assert.Equal(new[] { "Romantic", "Romantic" }, s.Rolled); // the refused third roll threw no dice
    }

    [Fact]
    public void KeepingTheFirstRollGivesUpTheReroll()
    {
        PageTests.Setup s = new();
        s.Dice.Enqueue(new[] { 10, 10, 10, 10, 10 });
        s.Ask(new { op = "roll", field = "trait.fearful" });

        JsonElement kept = s.Ask(new { op = "keep", field = "trait.fearful" });

        Assert.True(Field(kept, "trait.fearful").GetProperty("fixed").GetBoolean());
        Assert.False(s.Ask(new { op = "roll", field = "trait.fearful" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "keep", field = "trait.fearful" }).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ATraitKeepsItsValueWhenOnlyItsVisibilityChanges()
    {
        PageTests.Setup s = new();
        s.Ask(new { op = "set", field = "trait.proud", text = "70" });

        JsonElement a = s.Ask(new { op = "vis", field = "trait.proud", vis = "public" });

        Assert.Equal(70, Field(a, "trait.proud").GetProperty("number").GetInt32());
        Assert.True(Field(a, "trait.proud").GetProperty("fixed").GetBoolean());
        Assert.Equal("public", Field(a, "trait.proud").GetProperty("vis").GetString());
        string seen = s.Ask(new { op = "preview", level = "public" }).GetProperty("view").GetRawText();
        Assert.Contains("70 (strongly, chosen)", seen);
    }

    [Fact]
    public void ARollThatFailsCostsNothing()
    {
        PageTests.Setup s = new(); // no dice queued: the stand-in RpRoll throws

        JsonElement a = s.Ask(new { op = "roll", field = "trait.lazy" });

        Assert.False(a.GetProperty("ok").GetBoolean());
        Assert.True(Field(a, "trait.lazy").GetProperty("empty").GetBoolean());
    }

    [Fact]
    public void HomelandListsTheRacesOwnProvinceFirstThenTheRest()
    {
        PageTests.Setup s = new();
        s.Ask(new { op = "pick", field = "race", id = "race.khajiit" });

        JsonElement[] options = s.Ask(new { op = "options", field = "homeland" }).GetProperty("options").EnumerateArray().ToArray();

        Assert.Equal("Elsweyr", options[0].GetProperty("name").GetString());
        Assert.Equal("Native lands", options[0].GetProperty("group").GetString());
        Assert.All(options.Skip(1), o => Assert.Equal("Other Tamrielic Provinces", o.GetProperty("group").GetString()));
        Assert.Equal(new[] { "Cyrodiil", "Skyrim" }, options.Skip(1).Select(o => o.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public void AHomeRegionLiesInsideTheHomelandAndGoesWhenItChanges()
    {
        PageTests.Setup s = new();

        Assert.Equal(0, s.Ask(new { op = "options", field = "homeRegion" }).GetProperty("options").GetArrayLength());
        s.Ask(new { op = "pick", field = "homeland", id = "province.elsweyr" });
        string[] places = s.Ask(new { op = "options", field = "homeRegion" }).GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "Anequina", "Rimmen" }, places); // not the Heartlands of Cyrodiil

        Assert.True(s.Ask(new { op = "pick", field = "homeRegion", id = "settlement.rimmen" }).GetProperty("ok").GetBoolean());
        Assert.False(s.Ask(new { op = "pick", field = "homeRegion", id = "region.heartlands" }).GetProperty("ok").GetBoolean());

        JsonElement moved = s.Ask(new { op = "pick", field = "homeland", id = "province.cyrodiil" });
        Assert.True(Field(moved, "homeRegion").GetProperty("empty").GetBoolean());
        Assert.Contains("Home region cleared", moved.GetProperty("message").GetString());
    }

    [Fact]
    public void UpbringingFollowsTheRace()
    {
        PageTests.Setup s = new();
        s.Ask(new { op = "pick", field = "race", id = "race.khajiit" });

        string[] ways = s.Ask(new { op = "options", field = "upbringing" }).GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!).ToArray();

        Assert.Equal(new[] { "Born to a caravan", "Raised by a temple" }, ways);
        s.Ask(new { op = "pick", field = "upbringing", id = "khajiit.upbringing.caravan" });
        JsonElement nord = s.Ask(new { op = "pick", field = "race", id = "race.nord" });
        Assert.True(Field(nord, "upbringing").GetProperty("empty").GetBoolean());
    }
}
