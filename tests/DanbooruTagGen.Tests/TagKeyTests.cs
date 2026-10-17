using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>판정용 태그 키(TagKey)와, 그 키를 공통으로 쓰게 바꾼 충돌·중복·블록리스트·가중 추첨·
/// 순서 배치·역할 분류·검증. 레시피에는 순수 태그 외에 "(문장:1.1)", "(문장:1.1), real_tag",
/// "(vaginal:1.2)" 같은 문자열이 흔한데, 예전엔 전부 원문 통째 비교라 안의 태그를 못 봤다.</summary>
public class TagKeyTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    // ── 분해·정규화 ──────────────────────────────────────────────────────

    [Fact]
    public void CommaInsideParenthesesIsPreserved()
    {
        var f = Assert.Single(TagKey.Split("(she turns, glancing back over her shoulder:1.1)"));
        Assert.Equal("she turns, glancing back over her shoulder", f.Text);
        Assert.True(f.IsPhrase);
    }

    [Fact]
    public void BundleSplitsIntoSentenceAndCompanionTag()
    {
        var frags = TagKey.Split("(a fresh bite mark stands out, red and raw:1.1), bite_mark");
        Assert.Equal(2, frags.Count);
        Assert.Equal("a fresh bite mark stands out, red and raw", frags[0].Text);
        Assert.Equal("bite_mark", frags[1].Key);
        Assert.False(frags[1].IsPhrase);
    }

    [Theory]
    [InlineData("(vaginal:1.2)", "vaginal")]
    [InlineData("(vaginal:1.0~1.2)", "vaginal")]
    [InlineData("( vaginal : 1.1 ~ 1.3 )", "vaginal")]
    [InlineData("(piledriver_(sex):1.2)", "piledriver_(sex)")]
    [InlineData("piledriver_(sex)", "piledriver_(sex)")]
    [InlineData("Long Hair", "long_hair")]
    [InlineData("(long  hair:1.2)", "long_hair")]
    public void WeightSyntaxIsStrippedAndKeyNormalized(string raw, string key)
        => Assert.Equal(key, Assert.Single(TagKey.Split(raw)).Key);

    [Fact]
    public void RangeResolvedOrNotGivesSameKey()
    {
        // WeightRange.Resolve는 Add() 안에서 판정보다 먼저 돈다 — 전후 어느 쪽이든 키가 같아야 한다.
        var resolved = WeightRange.Resolve("(vaginal:1.0~1.2)", new FakeRandomSource(7));
        Assert.Equal(TagKey.Keys("(vaginal:1.0~1.2)"), TagKey.Keys(resolved));
    }

    [Fact]
    public void BareSentenceWithCommasIsNotSplitIntoWords()
    {
        // 괄호 없는 자연어 문장 속 쉼표는 문장 부호다 — "thick"가 태그로 둔갑하면 안 된다.
        var f = Assert.Single(TagKey.Split("thick, viscous cum dripping down her thighs"));
        Assert.True(f.IsPhrase);
    }

    [Fact]
    public void PlainTagListIsSplit()
        => Assert.Equal(new[] { "tube_top", "miniskirt" }, TagKey.Keys("tube_top, miniskirt"));

    [Fact]
    public void UnbalancedParenthesesAreLeftWhole()
        => Assert.Single(TagKey.Split("smile, :("));

    [Fact]
    public void ParenthesesThatDoNotWrapWholePieceAreNotStripped()
        => Assert.Equal("(a:1.1)_and_(b:1.2)", Assert.Single(TagKey.Split("(a:1.1) and (b:1.2)")).Key);

    // ── 1. 묶음 안의 동반 태그가 충돌 규칙에 걸린다 (n560 "풀넬슨 vs 손짓" 축소판) ──────

    private static ConflictRules HandsRules() => new(new[]
    {
        new ConflictGroup("그의 양손이 이미 다 쓰임(풀넬슨) vs 추가 손짓",
            new[] { "full_nelson", "grabbing_another's_breast", "hand_on_another's_hip" }),
    });

    private const string HandBundle = "(his hand roughly squeezes her breast, fingers sinking in:1.1), grabbing_another's_breast";

    [Fact]
    public void BundledCompanionTagConflictsWithFixedPose()
    {
        var rules = HandsRules();
        var line = new[] { "1girl", "full_nelson", HandBundle };

        Assert.True(rules.HasConflict(line));
        var hit = Assert.Single(rules.Detect(line));
        Assert.Equal(new[] { "full_nelson", "grabbing_another's_breast" }, hit.Tags);
    }

    [Fact]
    public void BundledCompanionTagIsSeenBySlotAnalysis()
    {
        var hits = HandsRules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "1girl", "full_nelson" }, 0, IsFixed: true),
            new SlotSpec(new[] { HandBundle, "(she bites her lip:1.1)" }, 1, IsFixed: false),
        });
        var hit = Assert.Single(hits);
        Assert.Equal(ConflictSeverity.Possible, hit.Severity);
    }

    [Fact]
    public void GenerationAvoidsBundleThatConflictsWithFixedPose()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Tags = { "1girl", "full_nelson" } },
                new RandomPoolSlot { Label = "그의 손", Tags = { HandBundle, "(she bites her lip:1.1)" }, MinCount = 1, MaxCount = 1 },
            },
        };
        var opts = new GenerationOptions { LineCount = 40, Seed = 3, AvoidDuplicateLines = false };
        var result = new WildcardGenerator().Generate(recipe, NoPools(), opts, HandsRules());

        Assert.Empty(result.Conflicts);
        Assert.DoesNotContain(result.Lines, l => l.Contains("squeezes her breast"));
    }

    [Fact]
    public void TwoMembersInsideOneBundleArePossibleEvenWithMaxCountOne()
    {
        // 묶음 하나가 뽑히면 조각이 전부 같이 나가므로, MaxCount=1이라도 안에서 충돌한다.
        var hits = HandsRules().AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "(he pins her:1.1), full_nelson, hand_on_another's_hip", "smile" }, 1, IsFixed: false),
        });
        Assert.Equal(ConflictSeverity.Possible, Assert.Single(hits).Severity);
    }

    // ── 2. 가중치 붙은 순수 태그도 충돌 규칙에 걸린다 ────────────────────────

    private static ConflictRules RealishRules() => new(new[]
    {
        new ConflictGroup("체위", new[] { "missionary", "doggystyle", "full_nelson" }),
        new ConflictGroup("삽입 부위", new[] { "vaginal", "anal" }, ConflictScope.Scene, new[] { "double_penetration" }),
        new ConflictGroup("표정 상충(쾌락 vs 공포)", new[] { "ahegao", "scared" }),
    });

    [Fact]
    public void WeightedTagsHitConflictRules()
    {
        var rules = RealishRules();
        Assert.True(rules.HasConflict(new[] { "(vaginal:1.2)", "anal" }));
        Assert.True(rules.HasConflict(new[] { "(ahegao:1.2)", "(scared:1.0~1.3)" }));
        Assert.True(rules.HasConflict(new[] { "(doggystyle:1.1)", "missionary" }));
        Assert.Equal("삽입 부위", Assert.Single(rules.Detect(new[] { "(vaginal:1.2)", "(anal:1.1)" })).Label);
    }

    [Fact]
    public void WeightedExceptTagStillSuppresses()
        => Assert.False(RealishRules().HasConflict(new[] { "(vaginal:1.2)", "anal", "(double_penetration:1.1)" }));

    [Fact]
    public void CsvMemberWithCommaInsideSentenceAndWeightedMembersLoadCorrectly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "입마개 vs 입 벌려 검사|bit_gag,(his hand grips her jaw, prying her mouth open:1.2)\n" +
                "테이블 vs 침대|(table:1.2),table,bed\n", System.Text.Encoding.UTF8);
            var rules = ConflictRules.LoadFromFile(path);

            Assert.Equal(2, rules.Groups[0].Tags.Count);   // 괄호 안 쉼표에서 안 잘림
            Assert.True(rules.HasConflict(new[] { "bit_gag", "(his hand grips her jaw, prying her mouth open:1.1)" }));
            // (table:1.2)와 table은 같은 키 — 하나만 있으면 충돌 아님, bed와 같이 있으면 충돌
            Assert.False(rules.HasConflict(new[] { "table" }));
            Assert.True(rules.HasConflict(new[] { "table", "bed" }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProseFragmentsNeverMatchRules()
        => Assert.False(RealishRules().HasConflict(new[] { "(she looks scared but moans:1.1)", "ahegao" }));

    // ── 3. 가중 추첨: 자연어·가중치 후보가 굶지 않는다 ────────────────────────

    // sqrt 가중치: common 10000→100, mid 2500→50, rare 100→10
    private static FakeTagLookup Freq() => new FakeTagLookup()
        .Add("common", 10_000).Add("mid", 2_500).Add("rare", 100).Add("zero", 0);

    [Fact]
    public void WeightOfStripsWeightAndUsesCompanionTag()
    {
        var look = Freq();
        Assert.Equal(100, WildcardGenerator.WeightOf("(common:1.2)", look));
        Assert.Equal(50, WildcardGenerator.WeightOf("(a sentence, with a comma:1.1), mid", look));
        // 실존 조각이 여럿이면 최댓값
        Assert.Equal(100, WildcardGenerator.WeightOf("(a sentence:1.1), rare, common", look));
        // 실존 조각이 없거나 빈도 0이면 null(풀 중앙값으로 채움)
        Assert.Null(WildcardGenerator.WeightOf("(she bites her lip:1.1)", look));
        Assert.Null(WildcardGenerator.WeightOf("zero", look));
        Assert.Null(WildcardGenerator.WeightOf("typo_tag", look));
    }

    [Fact]
    public void ProseCandidateGetsMedianOfKnownSiblings()
    {
        var weights = WildcardGenerator.SamplingWeights(new[]
        {
            "common", "mid", "rare", "(she bites her lip:1.1)", "(a bundle:1.1), mid",
        }, Freq());
        // 아는 값 [100, 50, 10, 50] → 정렬 [10, 50, 50, 100] → 중앙값 (50+50)/2 = 50
        Assert.Equal(new[] { 100, 50, 10, 50, 50 }, weights);
    }

    [Fact]
    public void MedianOfEvenCountRoundsAwayFromZero()
        => Assert.Equal(new[] { 100, 9, 55 },
            WildcardGenerator.SamplingWeights(new[] { "common", "(nine:1.1), x", "(prose:1.1)" },
                new FakeTagLookup().Add("common", 10_000).Add("x", 81)));   // [9, 100] → 54.5 → 55

    [Fact]
    public void AllProsePoolIsUniform()
        => Assert.Equal(new[] { 1, 1 },
            WildcardGenerator.SamplingWeights(new[] { "(one thing:1.1)", "(another:1.1)" }, Freq()));

    [Fact]
    public void ProseShareMatchesDesignedWeightsOverManyLines()
    {
        // 가중치 [100, 50, 10, 50, 50] → 자연어 2개 몫 = 100/260 = 38.5%.
        // 예전(자연어=1)이면 2/162 = 1.2%였다. 시드 고정이라 결정적(flaky 아님), 4000줄 → 표준오차 0.77%p.
        var recipe = new Recipe
        {
            Slots =
            {
                new RandomPoolSlot
                {
                    Label = "반응",
                    Tags = { "common", "mid", "rare", "(she bites her lip:1.1)", "(a tear rolls down, slowly:1.1)" },
                    MinCount = 1, MaxCount = 1,
                },
            },
        };
        var opts = new GenerationOptions
        {
            LineCount = 4000, Seed = 12345, Sampling = SamplingMode.Weighted,
            AvoidDuplicateLines = false, UnderscoreToSpace = false,
        };
        var lines = new WildcardGenerator().Generate(recipe, NoPools(), opts, null, Freq()).Lines;

        double prose = lines.Count(l => l.StartsWith('(')) / (double)lines.Count;
        Assert.InRange(prose, 0.385 - 0.03, 0.385 + 0.03);
    }

    // ── 4. 순서 배치: 가중치 태그도 같은 등급 ──────────────────────────────

    private static FakeTagLookup Ranks() => new FakeTagLookup()
        .Add("1girl", 0, "인원")
        .Add("long_hair", 0, "머리")
        .Add("huge_penis", 0, "신체")
        .Add("bite_mark", 0, "흔적", "분비물")
        .Add("forest", 0, "배경");

    [Fact]
    public void WeightedTagRanksLikeBareTag()
    {
        var order = new TagOrdering(Ranks());
        Assert.Equal(order.RankOf("huge_penis"), order.RankOf("(huge_penis:1.2)"));
        Assert.Equal(order.RankOf("huge_penis"), order.RankOf("(huge_penis:1.1~1.3)"));
        Assert.Equal(order.RankOf("bite_mark"), order.RankOf("(a fresh bite mark:1.1), bite_mark"));
        Assert.Equal(int.MaxValue, order.RankOf("(she bites her lip, trembling:1.1)"));
    }

    [Fact]
    public void ReorderMovesWeightedTagButPinsPureProse()
    {
        var order = new TagOrdering(Ranks());
        var result = order.Reorder(new[] { "forest", "(she bites her lip:1.1)", "(huge_penis:1.2)", "1girl" });
        // 자연어는 인덱스 1에 고정, 나머지는 인원(1) → 신체(3) → 배경(16)
        Assert.Equal(new[] { "1girl", "(she bites her lip:1.1)", "(huge_penis:1.2)", "forest" }, result);
    }

    // ── 줄 내 중복·블록리스트·출력 포맷 ─────────────────────────────────────

    [Fact]
    public void DedupeTreatsWeightedTagAsSameButKeepsBundleWithNewProse()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Tags = { "vaginal", "(vaginal:1.2)", "(she gasps:1.1), vaginal", "(she gasps:1.1)" } },
            },
        };
        var tags = new WildcardGenerator().GenerateLineTags(recipe, NoPools(),
            new GenerationOptions { DedupeWithinLine = true }, new FakeRandomSource());
        // (vaginal:1.2): 조각이 전부 이미 나옴 → 제거. 묶음: "she gasps"가 새 조각 → 원문 그대로 유지.
        // 마지막 "(she gasps:1.1)": 이미 나온 조각뿐 → 제거.
        Assert.Equal(new[] { "vaginal", "(she gasps:1.1), vaginal" }, tags);
    }

    [Fact]
    public void BlocklistCatchesWeightedAndBundledTags()
    {
        var recipe = new Recipe
        {
            Slots = { new FixedSlot { Tags = { "1girl", "(lowres:1.2)", "(a blurry photo:1.1), lowres", "solo" } } },
        };
        var tags = new WildcardGenerator().GenerateLineTags(recipe, NoPools(),
            new GenerationOptions { Blocklist = { "lowres" } }, new FakeRandomSource());
        Assert.Equal(new[] { "1girl", "solo" }, tags);
    }

    [Fact]
    public void OutputLineKeepsOriginalStringsVerbatim()
    {
        var recipe = new Recipe
        {
            Slots = { new FixedSlot { Tags = { "forest", "(a fresh bite mark, red:1.1), bite_mark", "1girl", "(huge_penis:1.2)" } } },
        };
        var opts = new GenerationOptions { LineCount = 1, UnderscoreToSpace = false, AutoOrderTags = true };
        var line = new WildcardGenerator().Generate(recipe, NoPools(), opts, null, Ranks()).Lines[0];
        // 정렬: 1girl(1) → huge_penis(3) → bite_mark(분비물 12) → forest(16). 문자열 자체는 원문 그대로.
        Assert.Equal("1girl, (huge_penis:1.2), (a fresh bite mark, red:1.1), bite_mark, forest", line);
    }

    // ── 역할 분류·검증 ─────────────────────────────────────────────────────

    [Fact]
    public void AltVisualDetectionSeesWeightedAndBundledTags()
    {
        var alt = new AlternativeSlot
        {
            Label = "심리 상태",
            Groups =
            {
                new AlternativeGroup { Tags = { "(she trembles:1.1)" } },
                new AlternativeGroup { Tags = { "(vaginal:1.2)" } },
                new AlternativeGroup { Tags = { "(he finishes inside:1.1), after_sex" } },
            },
        };
        Assert.Equal(SlotRole.Major, SlotRoleClassifier.Classify(alt));
    }

    [Fact]
    public void ValidatorChecksCompanionTagInsideBundle()
    {
        var recipe = new Recipe
        {
            Name = "t",
            Slots = { new FixedSlot { Tags = { "1girl", "1boy", "(a fresh bite mark:1.1), bite_markk" } } },
        };
        var look = new FakeTagLookup().Add("1girl", 1).Add("1boy", 1).Add("bite_mark", 1);
        var issue = Assert.Single(RecipeValidator.Validate(recipe, NoPools(), look));
        Assert.Equal(ValidationRule.UnknownTag, issue.Rule);
        Assert.Equal("bite_markk", issue.Subject);
    }

    [Fact]
    public void ValidatorSeesPartnerNeedInsideBundle()
    {
        var recipe = new Recipe
        {
            Name = "t",
            Slots = { new FixedSlot { Tags = { "1girl", "(her lips stretch wide:1.1), fellatio" } } },
        };
        var issue = Assert.Single(RecipeValidator.Validate(recipe, NoPools(), null));
        Assert.Equal(ValidationRule.MissingPartner, issue.Rule);
        Assert.Equal("fellatio", issue.Subject);
    }

    [Fact]
    public void ValidatorStillAcceptsPartnerWordInBundledSentence()
    {
        var recipe = new Recipe
        {
            Name = "t",
            Slots = { new FixedSlot { Tags = { "1girl", "(he holds her head down:1.1), fellatio" } } },
        };
        Assert.Empty(RecipeValidator.Validate(recipe, NoPools(), null));
    }
}
