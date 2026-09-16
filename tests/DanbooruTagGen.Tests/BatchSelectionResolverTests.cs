using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>저장해 둔 선택 목록을 현재 레시피 라이브러리에 맞춰 푸는 로직. 저장 이후 레시피가
/// 지워지거나 id가 바뀌었을 수 있어서, 매칭 실패를 어떻게 다룰지가 이 클래스의 핵심이다.
/// 잘못 판단하면 사용자 프리셋이 통째로 날아가므로 안전장치까지 테스트로 덮는다.</summary>
public class BatchSelectionResolverTests
{
    private static Recipe R(string id, string name) => new() { Id = id, Name = name };

    private static BatchSelectionPreset Preset(params (string Id, string Name)[] entries) => new()
    {
        Name = "프리셋",
        Entries = entries.Select(e => new BatchSelectionEntry { RecipeId = e.Id, RecipeName = e.Name }).ToList(),
    };

    [Fact]
    public void MatchesByIdFirst()
    {
        var library = new[] { R("a", "촉수"), R("b", "구속") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수")), library);
        Assert.Equal(new[] { "a" }, result.MatchedRecipeIds);
        Assert.Empty(result.MissingNames);
    }

    /// <summary>레시피 이름이 바뀌어도 id가 같으면 그대로 찾아야 한다.</summary>
    [Fact]
    public void RenamedRecipeStillMatchesById()
    {
        var library = new[] { R("a", "촉수 (개정판)") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수")), library);
        Assert.Equal(new[] { "a" }, result.MatchedRecipeIds);
        Assert.Empty(result.MissingNames);
    }

    /// <summary>번들 팩이 지워졌다 다른 id로 다시 들어오면 id 매칭이 깨진다. 이름으로 한 번 더
    /// 찾고, 찾으면 엔트리의 id를 새 값으로 갱신해 다음부터는 id로 바로 잡히게 한다.</summary>
    [Fact]
    public void FallsBackToNameAndUpdatesTheStoredId()
    {
        var library = new[] { R("new-id", "촉수"), R("x", "기타"), R("y", "기타2") };
        var result = BatchSelectionResolver.Resolve(Preset(("old-id", "촉수"), ("x", "기타")), library);
        Assert.Equal(new[] { "new-id", "x" }, result.MatchedRecipeIds);
        Assert.Empty(result.MissingNames);
        // 사라진 항목이 없어도 갱신된 id는 저장돼야 한다 — 아니면 매번 이름 폴백을 다시 탄다.
        Assert.True(result.ShouldSave);
        Assert.Contains(result.Entries, e => e.RecipeId == "new-id");
        Assert.DoesNotContain(result.Entries, e => e.RecipeId == "old-id");
    }

    [Fact]
    public void ReportsMissingEntriesByName()
    {
        var library = new[] { R("a", "촉수"), R("b", "구속"), R("c", "감금") };
        var preset = Preset(("a", "촉수"), ("b", "구속"), ("c", "감금"), ("gone", "사라진팩"));
        var result = BatchSelectionResolver.Resolve(preset, library);
        Assert.Equal(new[] { "사라진팩" }, result.MissingNames);
    }

    [Fact]
    public void PrunesWhenOnlyAFewAreMissing()
    {
        var library = new[] { R("a", "촉수"), R("b", "구속"), R("c", "감금") };
        var preset = Preset(("a", "촉수"), ("b", "구속"), ("c", "감금"), ("gone", "사라진팩"));
        var result = BatchSelectionResolver.Resolve(preset, library);
        Assert.False(result.PruningWithheld);
        Assert.True(result.ShouldSave);
        Assert.Equal(3, result.Entries.Count);
        Assert.DoesNotContain(result.Entries, e => e.RecipeId == "gone");
    }

    /// <summary>안전장치 1: 라이브러리가 통째로 안 읽혔을 수 있다(로드 실패, 번들 갱신 중).
    /// 이때 정리하면 프리셋이 통째로 날아간다.</summary>
    [Fact]
    public void DoesNotPruneWhenLibraryIsEmpty()
    {
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수"), ("b", "구속")), Array.Empty<Recipe>());
        Assert.True(result.PruningWithheld);
        Assert.False(result.ShouldSave);
        Assert.Equal(2, result.Entries.Count);
        Assert.Empty(result.MatchedRecipeIds);
        Assert.Equal(new[] { "촉수", "구속" }, result.MissingNames);
    }

    /// <summary>안전장치 2: 절반 이상이 안 맞으면 라이브러리 쪽이 비정상일 가능성이 높다.</summary>
    [Fact]
    public void DoesNotPruneWhenHalfOrMoreAreMissing()
    {
        var library = new[] { R("a", "촉수") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수"), ("x", "없음1"), ("y", "없음2")), library);
        Assert.True(result.PruningWithheld);
        Assert.False(result.ShouldSave);
        Assert.Equal(3, result.Entries.Count);
    }

    /// <summary>보류된 경우에도 찾아낸 레시피는 정상적으로 체크돼야 한다. 정리만 안 할 뿐이다.</summary>
    [Fact]
    public void StillMatchesEvenWhenPruningIsWithheld()
    {
        var library = new[] { R("a", "촉수") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수"), ("x", "없음1"), ("y", "없음2")), library);
        Assert.Single(result.MatchedRecipeIds);
        Assert.Equal("a", result.MatchedRecipeIds[0]);
    }

    [Fact]
    public void NothingMissingMeansNothingToPrune()
    {
        var library = new[] { R("a", "촉수") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수")), library);
        Assert.False(result.PruningWithheld);
        // 바뀐 게 없으면 저장할 이유도 없다.
        Assert.False(result.ShouldSave);
    }

    [Fact]
    public void DuplicateEntriesMatchOnlyOnce()
    {
        var library = new[] { R("a", "촉수") };
        var result = BatchSelectionResolver.Resolve(Preset(("a", "촉수"), ("a", "촉수")), library);
        Assert.Single(result.MatchedRecipeIds);
    }

    [Fact]
    public void EmptyPresetResolvesToNothing()
    {
        var library = new[] { R("a", "촉수") };
        var result = BatchSelectionResolver.Resolve(Preset(), library);
        Assert.Empty(result.MatchedRecipeIds);
        Assert.Empty(result.MissingNames);
        Assert.False(result.PruningWithheld);
        Assert.False(result.ShouldSave);
    }

    /// <summary>정확히 절반이 사라진 경우도 보류 쪽이다("절반 이상"이 기준).</summary>
    [Fact]
    public void ExactlyHalfMissingWithholdsPruning()
    {
        var library = new[] { R("a", "촉수"), R("b", "구속") };
        var preset = Preset(("a", "촉수"), ("b", "구속"), ("x", "없음1"), ("y", "없음2"));
        var result = BatchSelectionResolver.Resolve(preset, library);
        Assert.True(result.PruningWithheld);
        Assert.False(result.ShouldSave);
        Assert.Equal(4, result.Entries.Count);
    }
}
