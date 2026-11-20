using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Logging;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Tokenization;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>문제 로그(7일 보관) + 생성 결과에서 문제만 잘라 내기.</summary>
public sealed class ProblemLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dtg_log_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 11, 20, 15, 30, 12);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        var log = new ProblemLog(_dir);
        Assert.True(log.Write(new LogEntry(Now, LogKind.Concept, "생성 · 조합 바닥", "🔞 거부할 수 없는 매력의 저주 — 동의를 짜내는 함정", "요청 200줄 → 54줄\n이론 조합 54")));
        Assert.True(log.Write(new LogEntry(Now.AddSeconds(5), LogKind.Program, "번들 읽기 실패", null, "n593.json: JSON 오류")));
        var back = log.Read(Now.Date);
        Assert.Equal(2, back.Count);
        Assert.Equal(LogKind.Concept, back[0].Kind);
        Assert.Equal("🔞 거부할 수 없는 매력의 저주 — 동의를 짜내는 함정", back[0].Recipe);
        Assert.Equal("요청 200줄 → 54줄\n이론 조합 54", back[0].Body);
        Assert.Equal(Now, back[0].At);
        Assert.Null(back[1].Recipe);
        Assert.Equal(new[] { Now.Date }, log.Days());
    }

    [Fact]
    public void PruneKeepsSevenDaysIncludingToday()
    {
        var log = new ProblemLog(_dir);
        foreach (var d in new[] { 0, 6, 7, 30 })
            log.Write(new LogEntry(Now.AddDays(-d), LogKind.Program, "x", null, "y"));
        Assert.Equal(2, log.Prune(Now));
        Assert.Equal(new[] { Now.Date, Now.Date.AddDays(-6) }, log.Days());
    }

    [Fact]
    public void WriteFailureDoesNotThrow()
    {
        var log = new ProblemLog(_dir);
        log.Write(new LogEntry(Now, LogKind.Program, "first", null, "ok"));
        var path = Path.Combine(_dir, Now.ToString("yyyy-MM-dd") + ".log");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(log.Write(new LogEntry(Now, LogKind.Program, "second", null, "blocked")));
    }

    private static Recipe SixCombos() => new()
    {
        Id = "preset-r-small",
        Name = "작은 팩",
        Slots =
        {
            new AlternativeSlot { Label = "포즈", Groups = { new AlternativeGroup { Tags = { "standing" } }, new AlternativeGroup { Tags = { "sitting" } } } },
            new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "crying" } },
        },
    };

    private static GenerationProblemInput Input(Recipe recipe, GenerationResult result, int requested, int tokenLimit = 1000) =>
        new(recipe, new Dictionary<string, Pool>(), result, requested, IsPreview: false, Seed: 7,
            new TokenizerTests.LengthCounter(ModelKind.Anima), tokenLimit, result.Lines, Now);

    [Fact]
    public void ExhaustedRecipeProducesFormulaEntry()
    {
        var result = new WildcardGenerator().Generate(SixCombos(), new Dictionary<string, Pool>(),
            new GenerationOptions { LineCount = 200, Seed = 4, StopWhenExhausted = true });
        var entries = GenerationProblems.From(Input(SixCombos(), result, 200));
        var e = Assert.Single(entries);
        Assert.Equal(LogKind.Concept, e.Kind);
        Assert.Contains("조합 바닥", e.Title);
        Assert.Contains("요청 200줄 → 6줄", e.Body);
        Assert.Contains("포즈 2 × 표정 3", e.Body);
        Assert.Equal("작은 팩", e.Recipe);
    }

    [Fact]
    public void LeftoverConflictIncludesLineAndOrigin()
    {
        var rules = new ConflictRules(new[] { new ConflictGroup("시간대", new[] { "day", "night" }) });
        var recipe = new Recipe { Name = "모순 팩", Slots = { new FixedSlot { Label = "기본", Tags = { "day", "night" } }, new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush" } } } };
        var result = new WildcardGenerator().Generate(recipe, new Dictionary<string, Pool>(),
            new GenerationOptions { LineCount = 2, Seed = 1, CaptureOrigins = true, UnderscoreToSpace = false }, rules);
        var e = Assert.Single(GenerationProblems.From(Input(recipe, result, 2)));
        Assert.Contains("모순 남은 줄 2", e.Title);
        Assert.Contains(result.Lines[0], e.Body);
        Assert.Contains("시간대(day + night)", e.Body);
        Assert.Contains("[고정] 기본 : day, night", e.Body);
    }

    [Fact]
    public void CleanResultProducesNoEntries()
    {
        var result = new WildcardGenerator().Generate(SixCombos(), new Dictionary<string, Pool>(), new GenerationOptions { LineCount = 3, Seed = 2 });
        Assert.Empty(GenerationProblems.From(Input(SixCombos(), result, 3)));
    }

    [Fact]
    public void TokenOverLimitIsOneLinePerRecipe()
    {
        var result = new WildcardGenerator().Generate(SixCombos(), new Dictionary<string, Pool>(), new GenerationOptions { LineCount = 4, Seed = 2 });
        var e = Assert.Single(GenerationProblems.From(Input(SixCombos(), result, 4, tokenLimit: 5)));
        Assert.Contains("토큰 기준 초과", e.Title);
        Assert.DoesNotContain('\n', e.Body);
        Assert.Contains("기준 5 넘는 줄 4/4", e.Body);
    }

    /// <summary>같은 열쇠는 하루에 한 번만 새것 — 날이 바뀌면 다시 새것.</summary>
    [Fact]
    public void RepeatFilterPassesSameKeyOncePerDay()
    {
        var f = new RepeatFilter();
        Assert.True(f.IsNew("a", Now));
        Assert.False(f.IsNew("a", Now.AddHours(2)));
        Assert.True(f.IsNew("b", Now.AddHours(2)));
        Assert.True(f.IsNew("a", Now.AddDays(1)));
    }

    /// <summary>같은 레시피의 같은 문제는 시드가 달라도 같은 열쇠, 레시피를 고치면 다른 열쇠.</summary>
    [Fact]
    public void RepeatKeyIgnoresSeedButNotRecipeContent()
    {
        var opts = new GenerationOptions { LineCount = 200, StopWhenExhausted = true };
        var r1 = new WildcardGenerator().Generate(SixCombos(), new Dictionary<string, Pool>(), opts with { Seed = 4 });
        var r2 = new WildcardGenerator().Generate(SixCombos(), new Dictionary<string, Pool>(), opts with { Seed = 9 });
        var in1 = Input(SixCombos(), r1, 200) with { Seed = 4 };
        var in2 = Input(SixCombos(), r2, 200) with { Seed = 9 };
        var e1 = Assert.Single(GenerationProblems.From(in1));
        var e2 = Assert.Single(GenerationProblems.From(in2));
        Assert.NotEqual(e1.Title, e2.Title);   // 제목엔 시드가 들어간다
        Assert.Equal(GenerationProblems.RepeatKey(in1, e1), GenerationProblems.RepeatKey(in2, e2));

        var edited = SixCombos();
        ((RandomPoolSlot)edited.Slots[1]).Tags.Add("angry");
        var in3 = Input(edited, r1, 200);
        Assert.NotEqual(GenerationProblems.RepeatKey(in1, e1), GenerationProblems.RepeatKey(in3, e1));
        Assert.Equal(GenerationProblems.Exhausted, GenerationProblems.KindOf(e1));
    }
}
