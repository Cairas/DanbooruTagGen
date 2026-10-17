using System.Text;
using DanbooruTagGen.Core.Generation;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-3: conflicts.csv 확장 문법 — 편 나눔(<c>a,b vs c,d</c>)과 자원 예산(<c>@hands:2|tag=2,…</c>).
/// 기존 "그룹 전원 상호배타" 줄은 한 글자도 안 바꿔도 예전처럼 동작해야 한다.
/// 같은 문법을 tools/conflict_scan.py(parse_conflict_line/analyze_budget)가 파이썬으로 읽는다.</summary>
public class ConflictGrammarTests
{
    private static ConflictRules Load(params string[] lines)
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, string.Join("\n", lines), new UTF8Encoding(false));
            return ConflictRules.LoadFromFile(f);
        }
        finally { File.Delete(f); }
    }

    // ── 하위 호환 ───────────────────────────────────────────────

    [Fact]
    public void LegacyLinesStillMutuallyExclusive()
    {
        var rules = Load(
            "# 주석",
            "체위|missionary,doggystyle,full_nelson",
            "삽입 직전 vs 질내 사정|imminent_penetration,cum_in_pussy",   // 라벨의 " vs "는 문법이 아니다
            "눈 감음 vs 뜬 눈|closed_eyes,@_@");                           // "@_@" 멤버도 그대로
        Assert.Equal(3, rules.Count);
        Assert.All(rules.Groups, g => Assert.Equal(ConflictKind.Exclusive, g.Kind));
        Assert.True(rules.HasConflict(new[] { "missionary", "full_nelson" }));
        Assert.True(rules.HasConflict(new[] { "closed_eyes", "@_@" }));
        Assert.False(rules.HasConflict(new[] { "missionary", "cum_in_pussy" }));
    }

    [Fact]
    public void RequiresLinesAreNotConflictRules()
    {
        var rules = Load("requires|sex,vaginal|any-of|1boy,tentacles", "시간대|day,night");
        Assert.Equal(1, rules.Count);
        Assert.False(rules.HasConflict(new[] { "sex", "vaginal" }));
    }

    // ── 편 나눔 ─────────────────────────────────────────────────

    [Fact]
    public void SidesConflictOnlyAcrossSides()
    {
        var rules = Load("표정 상충(쾌락 vs 공포)|ahegao,torogao vs scared,screaming,wince");
        var g = Assert.Single(rules.Groups);
        Assert.Equal(ConflictKind.Sides, g.Kind);

        Assert.False(rules.HasConflict(new[] { "ahegao", "torogao" }));      // 같은 편
        Assert.False(rules.HasConflict(new[] { "scared", "wince" }));        // 같은 편
        Assert.True(rules.HasConflict(new[] { "torogao", "wince" }));        // 다른 편
        var hit = Assert.Single(rules.Detect(new[] { "ahegao", "torogao", "screaming" }));
        Assert.Equal(new[] { "ahegao", "torogao", "screaming" }, hit.Tags);
    }

    [Fact]
    public void SidesLineMatchesHandExpandedCrossProduct()
    {
        // 지금 conflicts.csv "표정 상충" 섹션처럼 교차곱을 줄마다 펼친 것과 판정이 같아야 한다.
        var oneLine = Load("상충|ahegao,torogao vs scared,grimace");
        var expanded = Load("상충|ahegao,scared", "상충|ahegao,grimace", "상충|torogao,scared", "상충|torogao,grimace");
        var all = new[] { "ahegao", "torogao", "scared", "grimace", "smile" };
        foreach (var a in all)
            foreach (var b in all)
                Assert.Equal(expanded.HasConflict(new[] { a, b }), oneLine.HasConflict(new[] { a, b }));
    }

    [Fact]
    public void ThreeSidesAndWeightedOrSentenceMembers()
    {
        var rules = Load("셋|a vs (b:1.2),(his hand, gripping:1.1) vs c");
        Assert.True(rules.HasConflict(new[] { "a", "c" }));
        Assert.True(rules.HasConflict(new[] { "(b:1.3)", "a" }));
        Assert.False(rules.HasConflict(new[] { "b", "(his hand, gripping:1.0)" }));   // 같은 편(괄호 안 쉼표는 구분자가 아님)
    }

    [Fact]
    public void SidesRespectScopeAndExcept()
    {
        var rules = Load("눈|blue_eyes vs red_eyes,green_eyes|character|heterochromia");
        Assert.True(rules.HasConflict(new[] { "blue_eyes", "red_eyes" }));
        Assert.False(rules.HasConflict(new[] { "2girls", "blue_eyes", "red_eyes" }));
        Assert.False(rules.HasConflict(new[] { "heterochromia", "blue_eyes", "red_eyes" }));
    }

    [Theory]
    [InlineData("겹침|a,b vs b,c")]   // 한 태그가 두 편 → 그 태그 하나로 항상 충돌하므로 버린다
    [InlineData("빈 편|a,b vs ")]          // 줄 끝 공백이 다듬어져 " vs"만 남는다
    [InlineData("빈 편|a,b vs  vs c")]     // 가운데 편이 빔
    public void MalformedSidesLineIsSkipped(string line) => Assert.Equal(0, Load(line).Count);

    [Fact]
    public void SlotAnalysisOfSidesMergesIntoOneRule()
    {
        var rules = Load("상충|ahegao,torogao vs scared");
        var hits = rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "scared" }, 1, IsFixed: true),
            new SlotSpec(new[] { "ahegao", "torogao", "smile" }, 1, IsFixed: false),
        });
        var hit = Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hit.Severity);
        Assert.Equal(new[] { "ahegao", "scared", "torogao" }, hit.Tags.OrderBy(t => t));

        // 같은 편 둘만 있으면 아무것도 안 나온다
        Assert.Empty(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "ahegao" }, 1, IsFixed: true),
            new SlotSpec(new[] { "torogao" }, 1, IsFixed: false),
        }));
    }

    // ── 자원 예산 ───────────────────────────────────────────────

    private const string HandsLine =
        "@hands:2 그의 양손|full_nelson=2,reverse_suspended_congress=2,grabbing_another's_breast,hand_on_another's_hip=1,hand_on_another's_waist";

    [Fact]
    public void BudgetLineParses()
    {
        var g = Assert.Single(Load(HandsLine).Groups);
        Assert.Equal(ConflictKind.Budget, g.Kind);
        Assert.Equal("그의 양손", g.Label);
        Assert.Equal("hands", g.Budget!.Resource);
        Assert.Equal(2, g.Budget.Limit);
        Assert.Equal(new[] { 2, 2, 1, 1, 1 }, g.Budget.Costs);
    }

    [Fact]
    public void BudgetConflictsOnlyWhenSumExceedsLimit()
    {
        var rules = Load(HandsLine);
        // 한 손 + 한 손 = 2 ≤ 2 → 괜찮다(기존 상호배타 줄은 이것까지 막았다).
        Assert.False(rules.HasConflict(new[] { "grabbing_another's_breast", "hand_on_another's_hip" }));
        // 풀넬슨(양손) + 한 손 = 3 > 2
        Assert.True(rules.HasConflict(new[] { "full_nelson", "(his hand grips her hip:1.1), hand_on_another's_hip" }));
        // 한 손 셋 = 3 > 2 — 쌍으로는 못 잡는 경우
        var hit = Assert.Single(rules.Detect(new[] { "grabbing_another's_breast", "hand_on_another's_hip", "hand_on_another's_waist" }));
        Assert.Equal(3, hit.Tags.Count);
    }

    [Fact]
    public void BudgetLabelDefaultsToResourceName()
    {
        var g = Assert.Single(Load("@mouth:1|fellatio,ball_gag,tongue_out").Groups);
        Assert.Equal("mouth 예산(1) 초과", g.Label);
    }

    [Theory]
    [InlineData("@hands:2|full_nelson=1.5,grabbing_another's_breast")]   // 소수 비용
    [InlineData("@hands:x|full_nelson=2")]                              // 숫자 아닌 예산
    [InlineData("@hands|full_nelson=2")]                                // 예산 없음
    public void MalformedBudgetLineIsSkipped(string line) => Assert.Equal(0, Load(line).Count);

    [Fact]
    public void BudgetMemberNamedWithEqualsSignKeepsItsName()
    {
        var g = Assert.Single(Load("@face:1|=_=,smile=1").Groups);
        Assert.Equal(new[] { "=_=", "smile" }, g.Tags);
        Assert.Equal(new[] { 1, 1 }, g.Budget!.Costs);
    }

    [Fact]
    public void SlotAnalysisOfBudget()
    {
        var rules = Load(HandsLine);

        // 고정 풀넬슨(2) + 손짓 풀(1) → 가끔 3 > 2
        var possible = Assert.Single(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "full_nelson" }, 1, IsFixed: true),
            new SlotSpec(new[] { "grabbing_another's_breast", "smile" }, 1, IsFixed: false),
        }));
        Assert.Equal(ConflictSeverity.Possible, possible.Severity);

        // 한 손짓 풀(MaxCount=1)에서 한 손 태그 둘 → 하나만 뽑히므로 1 ≤ 2, 충돌 아님
        Assert.Empty(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "grabbing_another's_breast", "hand_on_another's_hip" }, 1, IsFixed: false),
        }));

        // MaxCount=3이면 한 손 셋이 같이 나올 수 있다 → 3 > 2
        Assert.Single(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "grabbing_another's_breast", "hand_on_another's_hip", "hand_on_another's_waist" }, 3, IsFixed: false),
        }));

        // ALT 그룹 하나 안에서 이미 넘음 → Definite
        var definite = Assert.Single(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "full_nelson", "grabbing_another's_breast" }, 2, IsFixed: false, AltGroupOwnerId: 0),
            new SlotSpec(new[] { "standing_sex" }, 1, IsFixed: false, AltGroupOwnerId: 0),
        }));
        Assert.Equal(ConflictSeverity.Definite, definite.Severity);

        // 같은 ALT의 서로 다른 그룹은 같이 못 나온다 → 충돌 아님
        Assert.Empty(rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "full_nelson" }, 1, IsFixed: false, AltGroupOwnerId: 0),
            new SlotSpec(new[] { "grabbing_another's_breast" }, 1, IsFixed: false, AltGroupOwnerId: 0),
        }));
    }

    [Fact]
    public void BudgetExceptSuppresses()
    {
        var rules = Load("@hands:2|full_nelson=2,grabbing_another's_breast||sex_machine");
        Assert.False(rules.HasConflict(new[] { "full_nelson", "grabbing_another's_breast", "sex_machine" }));
        Assert.True(rules.HasConflict(new[] { "full_nelson", "grabbing_another's_breast" }));
    }

    [Fact]
    public void ConstructorRejectsMemberOnTwoSides() =>
        Assert.Throws<ArgumentException>(() => new ConflictRules(new[]
        {
            ConflictGroup.Bipartite("x", new IReadOnlyList<string>[] { new[] { "a", "b" }, new[] { "b" } }),
        }));
}
