using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class PresetSyncPreviewTests
{
    private static Recipe R(string id, string name, string tag) => new() { Id = id, Name = name, Slots = { new FixedSlot { Tags = { tag } } } };

    [Fact]
    public void PreviewListsAddedReplacedRemovedWithoutTouchingTheOriginals()
    {
        var a = R("a", "에이", "x");
        var b = R("b", "비", "x");
        var mine = R("c", "내 것", "x");
        var user = new List<Recipe> { a, b, mine };
        var bundle = new List<Recipe> { R("a", "에이", "changed"), R("d", "디", "x") };
        var seeded = new[] { "a", "b" };

        var (added, replaced, removed, _) = PresetSeeder.Preview(new List<Pool>(), new List<Pool>(), user, bundle, seeded);

        Assert.Equal(new[] { "디" }, added);
        Assert.Equal(new[] { "에이" }, replaced);
        Assert.Equal(new[] { "비" }, removed);
        // 원본은 그대로
        Assert.Equal(new[] { a, b, mine }, user);
        Assert.Equal("x", ((FixedSlot)a.Slots[0]).Tags[0]);
    }
}
