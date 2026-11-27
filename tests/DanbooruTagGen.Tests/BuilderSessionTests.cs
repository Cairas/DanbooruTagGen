using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Logging;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using DanbooruTagGen.Core.Tokenization;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>레시피 편집 탭(빌더)이 "어느 레시피를 편집 중인지"를 재시작·실행 사이에 잃지 않는지.</summary>
public sealed class BuilderSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dtg_builder_" + Guid.NewGuid().ToString("N"));

    public BuilderSessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static List<Slot> Slots() => new() { new FixedSlot { Label = "기본", Tags = { "1girl", "solo" } } };

    private static List<Recipe> Library() => new()
    {
        new Recipe { Id = "preset-r-n599", Name = "🔞 코스튬 파티 뒤 둘만의 밤", Slots = Slots() },
        new Recipe { Id = "other", Name = "다른 레시피" },
    };

    /// <summary>재시작해도 불러온 레시피와의 연결이 남아야 퀵 갱신이 편집 탭도 최신으로 맞춘다.</summary>
    [Fact]
    public void LinkedRecipeSurvivesRestart()
    {
        var path = Path.Combine(_dir, "last-recipe.json");
        RecipeStore.Save(BuilderSession.Autosave(Slots(), "preset-r-n599"), path);
        var restored = RecipeStore.Load(path);
        Assert.Equal("preset-r-n599", BuilderSession.LinkedId(restored, Library()));
        Assert.Single(restored.Slots);
    }

    [Fact]
    public void UnlinkedDeletedOrLegacyAutosaveRestoresWithoutLink()
    {
        Assert.Null(BuilderSession.LinkedId(BuilderSession.Autosave(Slots(), null), Library()));
        Assert.Null(BuilderSession.LinkedId(BuilderSession.Autosave(Slots(), "deleted-id"), Library()));
        // 예전 자동 저장본은 매번 새 무작위 id였다 — 아무 레시피와도 이어지지 않는다.
        Assert.Null(BuilderSession.LinkedId(new Recipe { Slots = Slots() }, Library()));
    }

    /// <summary>편집 탭 생성은 매번 같은 이름·id여야 같은 문제가 하루 한 번만 로그에 남는다(예전엔 실행마다 새 무작위 id라 매번 남았다).</summary>
    [Fact]
    public void BuilderRunsHaveStableIdentity()
    {
        var a = BuilderSession.ForRun(Slots(), "preset-r-n599", Library());
        var b = BuilderSession.ForRun(Slots(), "preset-r-n599", Library());
        Assert.Equal("preset-r-n599", a.Id);
        Assert.Equal("🔞 코스튬 파티 뒤 둘만의 밤 (편집 탭)", a.Name);
        Assert.Equal(Key(a), Key(b));

        var loose = BuilderSession.ForRun(Slots(), null, Library());
        Assert.Equal(BuilderSession.UnlinkedRunId, loose.Id);
        Assert.Equal(Key(loose), Key(BuilderSession.ForRun(Slots(), null, Library())));
        Assert.Equal(BuilderSession.UnlinkedRunId, BuilderSession.ForRun(Slots(), "deleted-id", Library()).Id);
    }

    private static string Key(Recipe recipe)
    {
        var result = new GenerationResult(new[] { "1girl, solo" }, Array.Empty<string>());
        var counter = TokenCounters.For(ModelKind.IL, Path.Combine(Path.GetTempPath(), "no-tokenizers"));
        var input = new GenerationProblemInput(recipe, new Dictionary<string, Pool>(), result, 100, false, 1, counter, 225,
            result.Lines, new DateTime(2026, 11, 26));
        return GenerationProblems.RepeatKey(input, new LogEntry(input.At, LogKind.Concept, "생성 · " + GenerationProblems.Exhausted, recipe.Name, ""));
    }

    /// <summary>미리보기는 잠근 줄과 별개로 새 줄을 몇 줄 보여 줘야 한다 — 잠근 줄이 미리보기 줄 수 이상이면 새 줄이 0이 됐다.</summary>
    [Fact]
    public void PreviewAlwaysDrawsFreshLinesBesideLocks()
    {
        Assert.Equal(13, RunLineCounts.Total(requested: 100, locked: 10, preview: true, previewEach: 3));
        Assert.Equal(3, RunLineCounts.Total(100, 0, true, 3));
        Assert.Equal(2, RunLineCounts.Total(2, 0, true, 3));
        Assert.Equal(100, RunLineCounts.Total(100, 10, false, 3));
    }
}
