using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-4: <c>implies:태그</c> — 출력 줄에는 안 나가고, 충돌·검증 판정에서만 그 태그가 있는 것처럼 본다.
/// voyeurism처럼 출력하면 그림이 왜곡되는 강한 태그를 판정에만 쓰려는 용도.</summary>
public class ImpliesTagTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    [Fact]
    public void SplitMarksImpliedFragment()
    {
        var frags = TagKey.Split("(seen through the gap in the door:1.1), implies:voyeurism");
        Assert.Equal(2, frags.Count);
        Assert.False(frags[0].IsImplied);
        Assert.True(frags[1].IsImplied);
        Assert.Equal("voyeurism", frags[1].Key);
    }

    [Fact]
    public void ImpliedPieceDoesNotTurnBareSentenceIntoWords()
    {
        // 괄호 없는 문장 뒤에 붙여도 문장은 통째로, implies:는 따로 잡힌다(규칙 3에서 빠진다).
        var frags = TagKey.Split("she peeks through, unseen, implies:voyeurism");
        Assert.Equal(new[] { "she_peeks_through,_unseen", "voyeurism" }, frags.Select(f => f.Key));
        Assert.Equal(new[] { false, true }, frags.Select(f => f.IsImplied));
    }

    [Fact]
    public void StandaloneImpliedFragment()
    {
        var f = Assert.Single(TagKey.Split("implies:voyeurism"));
        Assert.True(f.IsImplied);
        Assert.Equal("voyeurism", f.Text);
    }

    [Theory]
    [InlineData("(peeking:1.1), implies:voyeurism", "(peeking:1.1)")]
    [InlineData("implies:voyeurism", "")]
    [InlineData("Implies: voyeurism, smile", "smile")]
    [InlineData("3::smile", "smile")]
    [InlineData("(she turns,  glancing back:1.1)", "(she turns,  glancing back:1.1)")]   // 둘 다 없으면 원문 그대로
    public void OutputTextStripsJudgmentOnlyParts(string raw, string expected) =>
        Assert.Equal(expected, TagKey.OutputText(raw));

    [Fact]
    public void ImpliedTagIsNotOutputButIsSeenByConflictRules()
    {
        var rules = new ConflictRules(new[] { new ConflictGroup("엿보기 vs 정면 시선", new[] { "voyeurism", "looking_at_viewer" }) });
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Tags = { "1girl", "(seen through a gap in the door:1.1), implies:voyeurism" } },
                new RandomPoolSlot { Tags = { "looking_at_viewer", "looking_away" } },
            },
        };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 30, Seed = 3, AvoidDuplicateLines = false }, rules);

        Assert.All(result.Lines, l => Assert.DoesNotContain("implies", l));
        Assert.All(result.Lines, l => Assert.DoesNotContain("voyeurism", l));
        // 판정에서는 voyeurism이 있으므로 looking_at_viewer는 회피된다.
        Assert.All(result.Lines, l => Assert.DoesNotContain("looking at viewer", l));
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void ImpliedOnlyCandidateLeavesNoEmptySlotInLine()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "implies:voyeurism", "solo" } } } };
        var line = new WildcardGenerator().GenerateLine(recipe, NoPools(), new GenerationOptions(), new FakeRandomSource());
        Assert.Equal("1girl, solo", line);
    }

    [Fact]
    public void ImpliedTagDoesNotDedupeTheRealTagLater()
    {
        // 앞의 implies:voyeurism 때문에 뒤의 진짜 voyeurism 태그가 "줄 내 중복"으로 빠지면 안 된다.
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Tags = { "implies:voyeurism" } },
                new FixedSlot { Tags = { "voyeurism" } },
            },
        };
        var line = new WildcardGenerator().GenerateLine(recipe, NoPools(), new GenerationOptions(), new FakeRandomSource());
        Assert.Equal("voyeurism", line);
    }

    [Fact]
    public void ValidatorChecksImpliedTagExistenceAndCountsItAsPartner()
    {
        var dict = new FakeTagLookup().Add("1girl", 1).Add("fellatio", 1).Add("1boy", 1);
        var recipe = new Recipe
        {
            // 문장에 man/his 같은 상대 낱말이 없어야 implies:1boy 덕분에 통과하는지 볼 수 있다.
            Slots = { new FixedSlot { Tags = { "1girl", "fellatio", "(someone towers over her:1.0), implies:1boy", "implies:voyeurismm" } } },
        };
        var issues = RecipeValidator.Validate(recipe, NoPools(), dict);
        // implies:1boy가 상대로 인정되므로 MissingPartner 없음, 오탈자 implies:voyeurismm은 UnknownTag.
        var issue = Assert.Single(issues);
        Assert.Equal(ValidationRule.UnknownTag, issue.Rule);
        Assert.Equal("voyeurismm", issue.Subject);
    }

    [Fact]
    public void ImpliedTagDoesNotAffectFrequencyWeightOrOrdering()
    {
        var info = new FakeTagLookup().Add("voyeurism", 1_000_000, "시점");
        Assert.Null(WildcardGenerator.WeightOf("(peeking:1.1), implies:voyeurism", info));
        Assert.Equal(int.MaxValue, new TagOrdering(info).RankOf("(peeking:1.1), implies:voyeurism"));
    }
}
