using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-7: 상대 필요 규칙의 데이터화(data/requires.csv). RecipeValidator(C#)와 tools/conflict_scan.py가
/// 같은 파일을 읽는다. 둘의 의도된 차이(사후 흔적 태그는 앱 검증에서 뺌)는 strict 플래그로 보존한다.</summary>
public class RequiresRulesTests
{
    /// <summary>데이터화 전 RecipeValidator.NeedsPartner에 박혀 있던 표 그대로(회귀 방지).</summary>
    private static readonly string[] OldNeedsPartner =
    {
        "sitting_on_person", "hetero", "sex", "vaginal", "anal", "deep_penetration",
        "imminent_penetration", "penis_awe", "fellatio", "irrumatio", "deepthroat",
        "doggystyle", "missionary", "spooning", "sex_from_behind", "mating_press",
        "piledriver_(sex)", "prone_bone", "cowgirl_position", "girl_on_top",
        "standing_sex", "full_nelson", "suspended_congress", "double_penetration",
        "triple_penetration", "multiple_penetration", "spitroast", "knotting",
        "grabbing_another's_hair", "grabbing_another's_breast", "grabbing_another's_ass",
        "hand_on_another's_face", "hand_on_another's_head", "head_grab", "neck_grab",
        "strangling", "spanking", "thigh_grab", "torso_grab", "held_down",
    };

    private static readonly string[] OldPartnerMarkers =
    {
        "1boy", "2boys", "3boys", "4boys", "multiple_boys", "faceless_male",
        "2girls", "3girls", "multiple_girls", "multiple_others", "everyone",
        "monster", "monster_boy", "orc", "goblin", "oni", "demon", "minotaur",
        "tentacles", "penis_tentacle", "bestiality", "dog", "horse", "pig",
        "machinery", "sex_machine", "milking_machine", "penis", "huge_penis",
        "large_penis", "veiny_penis",
    };

    /// <summary>파이썬 스캐너에만 있던 사후 흔적 태그(앱 검증에서는 일부러 뺐던 것).</summary>
    private static readonly string[] TraceTags = { "ejaculation", "cum_in_pussy", "cum_in_mouth", "internal_cumshot" };

    [Fact]
    public void EmbeddedDefaultReproducesBothOldTables()
    {
        var rules = RequiresRules.Default.Rules;
        Assert.NotEmpty(rules);

        var appTags = rules.Where(r => !r.StrictOnly).SelectMany(r => r.Tags).ToHashSet();
        Assert.True(appTags.SetEquals(OldNeedsPartner), "앱 검증(non-strict) 태그가 예전 C# NeedsPartner와 달라졌다");

        var scannerTags = rules.SelectMany(r => r.Tags).ToHashSet();
        Assert.True(scannerTags.SetEquals(OldNeedsPartner.Concat(TraceTags)), "스캐너(전체) 태그가 예전 NEEDS_PARTNER와 달라졌다");

        Assert.All(rules, r => Assert.True(r.AnyOf.ToHashSet().SetEquals(OldPartnerMarkers)));
        Assert.All(rules.Where(r => r.Tags.Intersect(TraceTags).Any()), r => Assert.True(r.StrictOnly));
    }

    [Fact]
    public void ParseSkipsMalformedLines()
    {
        var rules = RequiresRules.Parse(new[]
        {
            "# 주석",
            "",
            "requires|leash|any-of|collar",
            "requires|leash|all-of|collar",          // 모르는 모드
            "requires||any-of|collar",                // 빈 태그
            "requires|leash|any-of|collar|strickt",   // 모르는 플래그(오타) → 줄째 버림
            "requires|leash|any-of|collar|strict",
            "라벨|a,b",                               // 다른 문법
        });
        Assert.Equal(2, rules.Rules.Count);
        Assert.False(rules.Rules[0].StrictOnly);
        Assert.True(rules.Rules[1].StrictOnly);
    }

    [Fact]
    public void ValidatorSkipsStrictRulesButUsesCustomRules()
    {
        var dict = new FakeTagLookup().Add("1girl", 1).Add("cum_in_pussy", 1).Add("leash", 1).Add("collar", 1);
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "cum_in_pussy", "leash" } } } };

        // 기본 표: cum_in_pussy는 strict라 앱 검증에서는 지적하지 않는다(사후 장면 오탐 방지 — 예전과 같음).
        Assert.Empty(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), dict));

        // 사용자 표: leash → collar 필요
        var custom = RequiresRules.Parse(new[] { "requires|leash|any-of|collar" });
        var issue = Assert.Single(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), dict, requires: custom));
        Assert.Equal(ValidationRule.MissingPartner, issue.Rule);
        Assert.Equal("leash", issue.Subject);
        Assert.Contains("collar", issue.Message);
    }

    [Fact]
    public void MessageKeepsTheOldExamples()
    {
        var dict = new FakeTagLookup().Add("1girl", 1).Add("fellatio", 1);
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "fellatio" } } } };
        var issue = Assert.Single(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), dict));
        Assert.Contains("1boy·tentacles·penis 등", issue.Message);
    }

    [Fact]
    public void LoadFromFileOrDefaultFallsBackToEmbedded()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        Assert.Same(RequiresRules.Default, RequiresRules.LoadFromFileOrDefault(missing));
    }
}
