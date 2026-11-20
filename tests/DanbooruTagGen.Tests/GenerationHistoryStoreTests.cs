using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public sealed class GenerationHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dtg_genhist_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static GenerationRecord Make(DateTime at, int recipes = 1) => new()
    {
        At = at,
        Seed = 42,
        Recipes = Enumerable.Range(0, recipes).Select(i => new GenerationRecordRecipe
        {
            Id = "r" + i, Name = "레시피_" + i, Requested = 2, Lines = { "a, b", "c, d" }, LineKeys = { "a\nb", "c\nd" },
        }).ToList(),
    };

    [Fact]
    public void RoundTripsAndSummarizesInTheFileName()
    {
        var at = new DateTime(2026, 11, 20, 15, 30, 12);
        GenerationHistoryStore.Save(_dir, Make(at));
        GenerationHistoryStore.Save(_dir, Make(at.AddMinutes(1), recipes: 3));
        var list = GenerationHistoryStore.List(_dir);
        Assert.Equal(2, list.Count);
        Assert.Equal("3개 레시피 6줄", list[0].Summary);
        Assert.Equal("레시피 0 2줄", list[1].Summary);   // 파일 이름의 '_'는 공백으로
        var back = GenerationHistoryStore.Load(list[1].Path)!;
        Assert.Equal(42, back.Seed);
        Assert.Equal(new[] { "a, b", "c, d" }, back.Recipes[0].Lines);
        Assert.Equal("레시피_0", back.Recipes[0].Name);
    }

    [Fact]
    public void KeepsOnlyTheNewest()
    {
        var at = new DateTime(2026, 11, 20, 10, 0, 0);
        for (int i = 0; i < GenerationHistoryStore.Keep + 3; i++) GenerationHistoryStore.Save(_dir, Make(at.AddMinutes(i)));
        var list = GenerationHistoryStore.List(_dir);
        Assert.Equal(GenerationHistoryStore.Keep, list.Count);
        Assert.Equal(at.AddMinutes(GenerationHistoryStore.Keep + 2), list[0].At);
    }
}
