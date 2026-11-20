using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class RecipeCheckReportTests
{
    private static readonly ConflictRules Rules = new(new[]
    {
        new ConflictGroup("시간대", new[] { "day", "night" }),
        new ConflictGroup("양손 다 씀 vs 손짓", new[] { "full_nelson", "grabbing_another's_breast", "hand_on_another's_hip" }),
    });

    private static Recipe Broken(params string[] fixedTags) => new()
    {
        Name = "점검용",
        Slots =
        {
            new FixedSlot { Label = "기본", Tags = new(fixedTags.Length > 0 ? fixedTags : new[] { "day" }) },
            new AlternativeSlot
            {
                Label = "포즈",
                Groups =
                {
                    new AlternativeGroup { Label = "풀넬슨", Tags = { "full_nelson" }, Weight = 1 },
                    new AlternativeGroup { Label = "서서", Tags = { "standing_sex" }, Weight = 2 },
                },
            },
            new RandomPoolSlot { Label = "손짓", Tags = { "grabbing_another's_breast", "hand_on_another's_hip" } },
        },
    };

    [Fact]
    public void ReportCoversConflictsCombinationsAndSuppression()
    {
        // 로컬 필터 끔: 줄 재추첨이 풀넬슨 브랜치를 사실상 없앤다(손짓이 전부 충돌).
        var opts = new GenerationOptions { LineCount = 200, Seed = 2, LocalConflictFiltering = false };
        var (summary, text) = RecipeCheckReport.Build(Broken(), new Dictionary<string, Pool>(), opts, Rules, tagInfo: null);

        Assert.Equal(0, summary.DefiniteConflicts);
        Assert.True(summary.Profile.IsTight);          // 서로 다른 줄 4종뿐
        Assert.Contains("[3] 조합·길이 — ⚠", text);
        Assert.Contains("포즈 / 풀넬슨", text);         // 명목 33% → 실측이 크게 깎임
        Assert.True(summary.MustFix >= 2);
        Assert.StartsWith("판정: 손볼 것", text.Split('\n')[^1]);
    }

    [Fact]
    public void FixedTagsThatAlwaysConflictAreReported()
    {
        var (summary, text) = RecipeCheckReport.Build(Broken("day", "night"), new Dictionary<string, Pool>(),
            new GenerationOptions { LineCount = 50, Seed = 2 }, Rules, tagInfo: null, deep: false);
        Assert.Equal(1, summary.DefiniteConflicts);
        Assert.Contains("[항상] 시간대: day + night", text);
    }

    [Fact]
    public void StageMarkersAreFineWhenRulesUseThemAndFlaggedWhenUnused()
    {
        var dict = new FakeTagLookup().Add("1girl", 1);
        var rules = new ConflictRules(new[] { new ConflictGroup("단계", new[] { "n9_gate", "n9_other" }) });
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "implies:n9_gate", "implies:n9_leftover", "implies:voyeurismm" } } } };
        var issues = RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), dict, conflicts: rules);
        Assert.DoesNotContain(issues, i => i.Subject == "n9_gate");
        Assert.Contains(issues, i => i.Rule == ValidationRule.UnusedMarker && i.Subject == "n9_leftover");
        Assert.Contains(issues, i => i.Rule == ValidationRule.UnknownTag && i.Subject == "voyeurismm");   // 표식 모양이 아니면 오탈자로
    }

    [Fact]
    public void MarkdownSummaryListsSlotsBranchesAndCandidates()
    {
        var md = Core.Output.RecipeMarkdown.Export(Broken(), new Dictionary<string, Pool>());
        Assert.StartsWith("## 점검용", md);
        Assert.Contains("### 1. [고정] 기본", md);
        Assert.Contains("- **풀넬슨** (weight 1, 33.3%, 태그 1개): `full_nelson`", md);
        Assert.Contains("### 3. [랜덤] 손짓 — 후보 2개 중 1개, 발동 100%", md);
        Assert.Contains("이론 조합 4", md);
    }

    [Fact]
    public void QuickCheckFlagsUnknownTagsAndSyntaxButNotPhrasesOrMarkers()
    {
        var dict = new FakeTagLookup().Add("smile", 1).Add("blush", 1);
        Assert.Null(RecipeValidator.QuickCheck("smile", dict));
        Assert.Null(RecipeValidator.QuickCheck("(she looks away shyly:1.1), blush", dict));   // 자연어 + 실존 태그
        Assert.Null(RecipeValidator.QuickCheck("implies:n12_stage", dict));                   // 단계 표식
        Assert.Contains("smlie", RecipeValidator.QuickCheck("smlie", dict));
        Assert.StartsWith("문법 오류", RecipeValidator.QuickCheck("(smile:1.2", dict));
    }

    [Fact]
    public void SlotPreviewShowsRatesCountsAndNeverPickedCandidates()
    {
        var slot = new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "0::crying" }, MinCount = 1, MaxCount = 1 };
        var text = SlotPreview.Describe(slot, new Dictionary<string, Pool>(), new GenerationOptions { Seed = 1 }, draws: 2000);
        Assert.Contains("[랜덤] 표정 — 후보 3개 중 1개", text);
        Assert.Contains("뽑힌 개수: 1개 100%", text);
        Assert.Contains("0::crying  ⛔ 한 번도 안 나옴", text);
        var alt = new AlternativeSlot { Label = "샷", Groups = { new() { Label = "가까이", Tags = { "close-up" }, Weight = 3 }, new() { Label = "멀리", Tags = { "full_body" }, Weight = 1 } } };
        Assert.Contains("75.0%  가까이 (weight 3): close-up", SlotPreview.Describe(alt, new Dictionary<string, Pool>(), new GenerationOptions()));
    }

    [Fact]
    public void ShallowModeSkipsTheRateAnalysis()
    {
        var (_, text) = RecipeCheckReport.Build(Broken(), new Dictionary<string, Pool>(),
            new GenerationOptions { LineCount = 50, Seed = 2 }, Rules, tagInfo: null, deep: false);
        Assert.DoesNotContain("[4]", text);
    }
}
