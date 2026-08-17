using System.Text;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class ConflictRulesTests
{
    private static ConflictRules Rules() => new(new[]
    {
        new ConflictGroup("머리 길이", new[] { "short_hair", "long_hair", "very_long_hair" }),
        new ConflictGroup("인원수", new[] { "1girl", "2girls", "3girls" }),
    });

    [Fact]
    public void DetectsTwoFromSameGroup()
    {
        var hits = Rules().Detect(new[] { "long_hair", "short_hair", "smile" });
        Assert.Single(hits);
        Assert.Equal("머리 길이", hits[0].Label);
        Assert.Equal(new[] { "short_hair", "long_hair" }, hits[0].Tags);
    }

    [Fact]
    public void NoConflictWhenSingleMember()
    {
        Assert.Empty(Rules().Detect(new[] { "long_hair", "1girl", "smile" }));
        Assert.False(Rules().HasConflict(new[] { "long_hair", "1girl" }));
    }

    [Fact]
    public void DetectsAcrossMultipleGroups()
    {
        var hits = Rules().Detect(new[] { "long_hair", "short_hair", "1girl", "2girls" });
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void AnalyzeRecipeDefiniteWhenBothAlwaysPresent()
    {
        var hits = Rules().AnalyzeRecipe(
            alwaysTags: new[] { "long_hair", "short_hair" },
            reachableTags: new[] { "long_hair", "short_hair" });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Definite, hits[0].Severity);
    }

    [Fact]
    public void AnalyzeRecipePossibleWhenOnlyReachable()
    {
        // 고정엔 long_hair만, 풀 후보에 short_hair → 가끔 충돌 가능
        var hits = Rules().AnalyzeRecipe(
            alwaysTags: new[] { "long_hair" },
            reachableTags: new[] { "long_hair", "short_hair" });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hits[0].Severity);
    }

    // ── 슬롯 단위(MaxCount 인식) 사전 점검 ──────────────────────────────
    [Fact]
    public void SlotAnalyzeNoConflictWithinSinglePickPool()
    {
        // 한 랜덤 슬롯(MaxCount=1)에 서로 배타인 태그가 다 들어 있어도 하나만 뽑히므로 충돌 아님.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair", "long_hair", "very_long_hair" }, MaxCount: 1, IsFixed: false),
        });
        Assert.Empty(hits);
    }

    [Fact]
    public void SlotAnalyzePossibleWhenSamePoolMultiPick()
    {
        // 같은 슬롯이라도 MaxCount≥2면 배타 태그 둘이 함께 뽑힐 수 있어 '가끔' 충돌.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair", "long_hair" }, MaxCount: 2, IsFixed: false),
        });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hits[0].Severity);
    }

    [Fact]
    public void SlotAnalyzePossibleAcrossTwoSlots()
    {
        // 서로 다른 두 슬롯이 각각 배타 태그를 내면 함께 나올 수 있어 '가끔' 충돌.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair" }, MaxCount: 1, IsFixed: false),
            new SlotSpec(new[] { "long_hair" }, MaxCount: 1, IsFixed: false),
        });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hits[0].Severity);
    }

    [Fact]
    public void SlotAnalyzeDefiniteWhenBothFixed()
    {
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair", "long_hair" }, MaxCount: 0, IsFixed: true),
        });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Definite, hits[0].Severity);
    }

    [Fact]
    public void SlotAnalyzeSuppressedByMultiPersonInFixed()
    {
        // 캐릭터 범위 충돌(머리 길이)이라도 고정에 다인수 마커가 있으면 억제.
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("머리 길이", new[] { "short_hair", "long_hair" }, ConflictScope.Character),
        });
        var hits = rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "2girls" }, MaxCount: 0, IsFixed: true),
            new SlotSpec(new[] { "short_hair", "long_hair" }, MaxCount: 2, IsFixed: false),
        });
        Assert.Empty(hits);
    }

    [Fact]
    public void SlotAnalyzeNoConflictAcrossDifferentAltGroups()
    {
        // 같은 ALT 슬롯(같은 AltGroupOwnerId)의 서로 다른 그룹은 정확히 하나만 뽑히므로
        // 그 그룹들의 태그끼리는 절대 같이 나올 수 없다 — 충돌로 잡히면 안 된다.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair" }, MaxCount: 1, IsFixed: false, AltGroupOwnerId: 0),
            new SlotSpec(new[] { "long_hair" }, MaxCount: 1, IsFixed: false, AltGroupOwnerId: 0),
        });
        Assert.Empty(hits);
    }

    [Fact]
    public void SlotAnalyzeDefiniteWhenSameAltGroupHasTwoMembers()
    {
        // 한 ALT 그룹 안에 배타 태그 2개가 같이 있으면, 그 그룹이 뽑힐 때마다 항상 같이 나온다.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair", "long_hair" }, MaxCount: 2, IsFixed: false, AltGroupOwnerId: 0),
        });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Definite, hits[0].Severity);
    }

    [Fact]
    public void SlotAnalyzePossibleAcrossDifferentAltSlots()
    {
        // 서로 다른 ALT 슬롯(AltGroupOwnerId가 다름)의 태그는 독립적으로 뽑히므로 가끔 공존 가능.
        var hits = Rules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "short_hair" }, MaxCount: 1, IsFixed: false, AltGroupOwnerId: 0),
            new SlotSpec(new[] { "long_hair" }, MaxCount: 1, IsFixed: false, AltGroupOwnerId: 1),
        });
        Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hits[0].Severity);
    }

    [Fact]
    public void LoadFromFileParsesGroupsAndSkipsComments()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f,
                "# 주석\n머리 길이|short_hair,long_hair\n\n입|open_mouth,closed_mouth\nbadline_no_pipe_single\n",
                new UTF8Encoding(false));
            var rules = ConflictRules.LoadFromFile(f);
            Assert.Equal(2, rules.Count); // 주석/빈줄/단일토큰 제외
            Assert.True(rules.HasConflict(new[] { "open_mouth", "closed_mouth" }));
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void CharacterScopeSuppressedWhenMultiPerson()
    {
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("눈 색", new[] { "blue_eyes", "red_eyes" }, ConflictScope.Character),
        });
        // 1인: 두 눈 색은 충돌
        Assert.True(rules.HasConflict(new[] { "1girl", "blue_eyes", "red_eyes" }));
        // 2girls: 서로 다른 인물의 속성일 수 있어 억제 → 충돌 아님
        Assert.False(rules.HasConflict(new[] { "2girls", "blue_eyes", "red_eyes" }));
        Assert.Empty(rules.Detect(new[] { "multiple_girls", "blue_eyes", "red_eyes" }));
    }

    [Fact]
    public void SceneScopeAlwaysConflictsRegardlessOfPersonCount()
    {
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("시간대", new[] { "day", "night" }), // 기본 scene
        });
        // 다인수여도 한 장면이 낮이면서 밤일 수는 없다 → 항상 충돌
        Assert.True(rules.HasConflict(new[] { "2girls", "day", "night" }));
    }

    [Fact]
    public void ExceptTagDisablesGroup()
    {
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("눈 색", new[] { "blue_eyes", "red_eyes" }, ConflictScope.Character,
                Except: new[] { "heterochromia" }),
        });
        // heterochromia가 있으면 두 눈 색이 정상 → 면제
        Assert.False(rules.HasConflict(new[] { "1girl", "heterochromia", "blue_eyes", "red_eyes" }));
        // 없으면 그대로 충돌
        Assert.True(rules.HasConflict(new[] { "1girl", "blue_eyes", "red_eyes" }));
    }

    [Fact]
    public void LoadFromFileParsesScopeAndExcept()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f,
                "눈 색|blue_eyes,red_eyes|character|heterochromia\n시간대|day,night\n",
                new UTF8Encoding(false));
            var rules = ConflictRules.LoadFromFile(f);
            Assert.Equal(2, rules.Count);
            // 캐릭터 스코프 + except 반영
            Assert.False(rules.HasConflict(new[] { "2girls", "blue_eyes", "red_eyes" }));
            Assert.False(rules.HasConflict(new[] { "heterochromia", "blue_eyes", "red_eyes" }));
            // scene은 항상
            Assert.True(rules.HasConflict(new[] { "2girls", "day", "night" }));
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void GeneratorReportsUnavoidableConflictInFixedSlot()
    {
        // 고정 슬롯에 모순 태그가 둘 다 → 회피 불가, 모든 줄이 모순으로 보고됨.
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "long_hair", "short_hair" } } } };
        var opts = new GenerationOptions { LineCount = 3, AvoidConflicts = true };
        var result = new WildcardGenerator().Generate(recipe, new Dictionary<string, Pool>(), opts, Rules());

        Assert.Equal(3, result.Conflicts.Count);              // 3줄 모두 모순
        Assert.Contains(result.Warnings, w => w.Contains("모순"));
    }
}
