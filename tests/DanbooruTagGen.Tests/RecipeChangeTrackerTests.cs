using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class RecipeChangeTrackerTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0);

    [Fact]
    public void NewRecipesAreThoseNotPresentBefore()
    {
        var marks = new Dictionary<string, RecipeChangeMark>();
        var after = new[] { new Recipe { Id = "a" }, new Recipe { Id = "b" }, new Recipe { Id = "c" } };
        Assert.Equal(2, RecipeChangeTracker.MarkNew(marks, new[] { "a" }, after, Now));
        Assert.Equal("🆕", RecipeChangeTracker.Badge(marks, "b"));
        Assert.Equal("", RecipeChangeTracker.Badge(marks, "a"));
    }

    [Fact]
    public void UpdatedKeepsAnUnseenNewMarkAsNew()
    {
        var marks = new Dictionary<string, RecipeChangeMark>
        {
            ["fresh"] = new() { Kind = RecipeChangeKind.New, At = Now.AddDays(-1) },
        };
        RecipeChangeTracker.MarkUpdated(marks, new[] { "fresh", "old" }, Now);
        Assert.Equal("🆕", RecipeChangeTracker.Badge(marks, "fresh"));
        Assert.Equal(Now, marks["fresh"].At);   // 갱신 시각은 새로 — 만료가 늦춰진다
        Assert.Equal("✏️", RecipeChangeTracker.Badge(marks, "old"));
    }

    [Fact]
    public void PruneDropsRemovedAndStaleMarks()
    {
        var marks = new Dictionary<string, RecipeChangeMark>
        {
            ["keep"] = new() { At = Now.AddDays(-3) },
            ["gone"] = new() { At = Now },
            ["stale"] = new() { At = Now - RecipeChangeTracker.MaxAge - TimeSpan.FromHours(1) },
        };
        Assert.Equal(2, RecipeChangeTracker.Prune(marks, new[] { "keep", "stale" }, Now));
        Assert.Equal(new[] { "keep" }, marks.Keys);
    }
}
