using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public sealed class RecipeHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dtg_hist_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 11, 20, 10, 0, 0);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Recipe Make(string tag) => new() { Id = "preset-r-n1", Name = "테스트", Slots = { new FixedSlot { Tags = { tag } } } };

    [Fact]
    public void SameContentIsNotRecordedTwice()
    {
        Assert.True(RecipeHistoryStore.Record(_dir, Make("a"), "기준", T0));
        Assert.False(RecipeHistoryStore.Record(_dir, Make("a"), "기준", T0.AddHours(1)));
        Assert.Single(RecipeHistoryStore.List(_dir, "preset-r-n1"));
    }

    /// <summary>선택할 때 잡아 둔 직렬화 문자열을 첫 편집 때 기준 버전으로 남긴다 — 같은 기준을 다시 남겨도 하나뿐.</summary>
    [Fact]
    public void BaselineFromJsonIsRecordedOnce()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Make("a"), JsonStore.Options);
        Assert.True(RecipeHistoryStore.Record(_dir, json, "preset-r-n1", RecipeHistoryStore.BaselineReason, T0));
        Assert.False(RecipeHistoryStore.Record(_dir, json, "preset-r-n1", RecipeHistoryStore.BaselineReason, T0.AddMinutes(1)));
        // Recipe로 남긴 같은 내용과도 같은 것으로 본다(직렬화 방식이 같다).
        Assert.False(RecipeHistoryStore.Record(_dir, Make("a"), RecipeHistoryStore.BaselineReason, T0.AddMinutes(2)));
        var list = RecipeHistoryStore.List(_dir, "preset-r-n1");
        Assert.Single(list);
        Assert.Equal("a", ((FixedSlot)RecipeHistoryStore.Load(list[0])!.Slots[0]).Tags[0]);
    }

    [Fact]
    public void QuickSuccessiveEditsCoalesceIntoOneVersion()
    {
        RecipeHistoryStore.Record(_dir, Make("a"), "기준", T0);
        RecipeHistoryStore.Record(_dir, Make("b"), RecipeHistoryStore.EditReason, T0.AddMinutes(1));
        RecipeHistoryStore.Record(_dir, Make("c"), RecipeHistoryStore.EditReason, T0.AddMinutes(2));
        var list = RecipeHistoryStore.List(_dir, "preset-r-n1");
        Assert.Equal(2, list.Count);
        Assert.Equal("c", ((FixedSlot)RecipeHistoryStore.Load(list[0])!.Slots[0]).Tags[0]);
        Assert.Equal("기준", list[1].Reason);

        // 창 밖의 편집은 새 버전
        RecipeHistoryStore.Record(_dir, Make("d"), RecipeHistoryStore.EditReason, T0.AddMinutes(30));
        Assert.Equal(3, RecipeHistoryStore.List(_dir, "preset-r-n1").Count);
    }

    [Fact]
    public void OldVersionsArePruned()
    {
        for (int i = 0; i < RecipeHistoryStore.KeepPerRecipe + 5; i++)
            RecipeHistoryStore.Record(_dir, Make("t" + i), "프리셋 갱신", T0.AddMinutes(i * 10));
        var list = RecipeHistoryStore.List(_dir, "preset-r-n1");
        Assert.Equal(RecipeHistoryStore.KeepPerRecipe, list.Count);
        Assert.Equal(T0.AddMinutes((RecipeHistoryStore.KeepPerRecipe + 4) * 10), list[0].Time);
        Assert.Equal(list[0].Time, RecipeHistoryStore.LastChanged(_dir, "preset-r-n1"));
    }

    [Fact]
    public void BaselineVersionsDoNotCountAsChanges()
    {
        RecipeHistoryStore.Record(_dir, Make("a"), RecipeHistoryStore.EditReason, T0);
        RecipeHistoryStore.Record(_dir, Make("b"), RecipeHistoryStore.BaselineReason, T0.AddHours(1));
        Assert.Equal(T0, RecipeHistoryStore.LastChanged(_dir, "preset-r-n1"));
    }
}
