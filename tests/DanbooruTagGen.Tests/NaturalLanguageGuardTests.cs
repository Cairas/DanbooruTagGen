using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>자연어 처리 점검 2-4/2-5/2-9/2-10.</summary>
public class NaturalLanguageGuardTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    private static Recipe FixedRecipe(params string[] tags)
    {
        var slot = new FixedSlot { Label = "기본" };
        foreach (var t in tags) slot.Tags.Add(t);
        return new Recipe { Name = "테스트", Slots = { slot } };
    }

    // ── 2-4: UI 쉼표 목록 입력 ──────────────────────────────────

    [Theory]
    [InlineData("1girl, (she is dragged in, still in shock:1.2), blush", new[] { "1girl", "(she is dragged in, still in shock:1.2)", "blush" })]
    [InlineData("(she is dragged in, still in shock:1.2)", new[] { "(she is dragged in, still in shock:1.2)" })]
    [InlineData("smile, :(", new[] { "smile", ":(" })]
    // 이모티콘이 섞여도 괄호 안 문장 쉼표는 보존(예전 SplitTopLevel은 짝 불일치로 폴백해 쪼갰다).
    [InlineData("(a, b:1.2), :(", new[] { "(a, b:1.2)", ":(" })]
    [InlineData("piledriver_(sex), gloom_(expression)", new[] { "piledriver_(sex)", "gloom_(expression)" })]
    public void SplitListKeepsCommasInsideParens(string input, string[] expected) =>
        Assert.Equal(expected, TagKey.SplitList(input));

    [Theory]
    [InlineData("1girl, (she is dragged in, still", false)]   // 입력 도중 — 아직 안 닫힘
    [InlineData("1girl, (she is dragged in, still in shock:1.2)", true)]
    [InlineData("smile, :(, ;), >:(", true)]   // 이모티콘은 짝 검사 대상 아님
    [InlineData("piledriver_\\(sex\\)", true)]   // 이스케이프된 괄호도 아님
    [InlineData("(tag:1.2))", false)]
    public void HasBalancedParensIgnoresEmoticons(string input, bool expected) =>
        Assert.Equal(expected, TagKey.HasBalancedParens(input));

    [Theory]
    [InlineData("1girl, sm", 5)]
    [InlineData("1girl, (she is dragged in, sti", 5)]   // 괄호 안 쉼표는 항목 구분이 아니다
    [InlineData("(she is dragged in, sti", -1)]
    [InlineData("(a, b:1.2), sm", 10)]
    [InlineData("long_hair", -1)]
    public void LastTopLevelCommaSkipsCommasInOpenParens(string input, int expected) =>
        Assert.Equal(expected, TagKey.LastTopLevelComma(input));

    [Fact]
    public void InputErrorRejectsBrokenWeightButNotPhrases()
    {
        Assert.NotNull(TagKey.FindInputError("(vaginal:1,2)"));
        Assert.NotNull(TagKey.FindInputError("(wide_hips:1.3~1.1)"));   // 기존 범위 오류도 같이
        Assert.Null(TagKey.FindInputError("(she is dragged in, still in shock:1.2)"));
        Assert.Null(TagKey.FindInputError(":("));
    }

    // ── 2-5: 괄호·가중치 문법 가드 ─────────────────────────────

    [Theory]
    [InlineData("smile")]
    [InlineData("she looks back over her shoulder")]
    [InlineData(":(")]
    [InlineData(">:(")]
    [InlineData(";)")]
    [InlineData("smile, :)")]
    [InlineData("piledriver_(sex)")]
    [InlineData("(piledriver_(sex):1.2)")]
    [InlineData("(wide_hips:1.1~1.3)")]
    [InlineData("3::(vaginal:1.2)")]
    [InlineData("(she is dragged in, still in shock:1.2)")]
    [InlineData("(she whispers: please~)")]   // 문장 속 콜론+물결
    [InlineData("(she moans: ah~ ah~:1.2)")]
    [InlineData("(masterpiece)")]
    [InlineData("((best quality))")]
    [InlineData("evangelion:_3.0_you_can_(not)_redo")]
    public void SyntaxCheckDoesNotFlagValidStrings(string raw) => Assert.Null(TagKey.FindSyntaxError(raw));

    [Theory]
    [InlineData("(vaginal:1,2)", "숫자")]
    [InlineData("(vaginal:1.2.3)", "숫자")]
    [InlineData("(vaginal;1.2)", "구분자")]
    [InlineData("(vaginal：1.2)", "구분자")]
    [InlineData("(she says: hi;1.2)", "구분자")]
    [InlineData("(vaginal:1.2", "짝")]
    [InlineData("vaginal:1.2)", "짝")]
    [InlineData("((vaginal:1.2)", "짝")]
    [InlineData("(:1.2)", "본문")]
    public void SyntaxCheckFlagsBrokenStrings(string raw, string expectedWord)
    {
        var error = TagKey.FindSyntaxError(raw);
        Assert.NotNull(error);
        Assert.Contains(expectedWord, error);
    }

    [Fact]
    public void ValidatorWarnsOnMalformedSyntaxWithLocation()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Label = "기본", Tags = { "1girl", "smile, :(", "(she whispers: please~)" } },
                new RandomPoolSlot { Label = "체위", Tags = { "(missionary:1,2)", "(cowgirl_position:1.2)" } },
                new AlternativeSlot
                {
                    Label = "표정",
                    Groups = { new AlternativeGroup { Label = "울음", Tags = { "(crying;1.2)" } } },
                },
            },
        };
        var issues = RecipeValidator.Validate(recipe, NoPools(), tagInfo: null)
            .Where(i => i.Rule == ValidationRule.MalformedSyntax).ToList();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Subject == "(missionary:1,2)" && i.Message.Contains("체위"));
        Assert.Contains(issues, i => i.Subject == "(crying;1.2)" && i.Message.Contains("울음"));
    }

    [Theory]
    [InlineData("(she whimpers softly: ah~)")]
    [InlineData("(her voice breaks: nn~ please~)")]
    public void ColonTildeInsidePhraseIsNotARange(string phrase)
    {
        // 예전: RangeTail이 ": ah~)"를 범위로 오인 → FindError → Validate 예외 → 레시피 생성 전체 실패.
        Assert.Null(WeightRange.FindError(phrase));
        var rnd = new FakeRandomSource(7, 7, 7);
        Assert.Equal(phrase, WeightRange.Resolve(phrase, rnd));

        var gen = new WildcardGenerator();
        var result = gen.Generate(FixedRecipe("1girl", phrase), NoPools(),
            new GenerationOptions { LineCount = 1, Seed = 1, UnderscoreToSpace = false });
        Assert.Equal("1girl, " + phrase, Assert.Single(result.Lines));
    }

    [Theory]
    [InlineData("(wide_hips:a~1.3)")]   // 한쪽이 숫자면 여전히 범위 오타
    [InlineData("(wide_hips:1.1~1,3)")]
    [InlineData("(wide_hips:~1.3)")]
    public void RealRangeTyposAreStillCaught(string tag) => Assert.NotNull(WeightRange.FindError(tag));

    // ── 2-9: 출력 괄호 이스케이프 ──────────────────────────────

    [Theory]
    [InlineData("piledriver_(sex)", @"piledriver_\(sex\)")]
    [InlineData("gloom_(expression)", @"gloom_\(expression\)")]
    [InlineData("(piledriver_(sex):1.2)", @"(piledriver_\(sex\):1.2)")]   // 바깥 가중치 괄호는 유지, 안쪽만
    [InlineData("(vaginal:1.2)", "(vaginal:1.2)")]
    [InlineData("((vaginal:1.1):1.2)", "((vaginal:1.1):1.2)")]
    [InlineData("(she is dragged in, still in shock:1.2)", "(she is dragged in, still in shock:1.2)")]
    [InlineData("(a fresh bite mark:1.1), cheating_(relationship)", @"(a fresh bite mark:1.1), cheating_\(relationship\)")]
    [InlineData(":)", @":\)")]
    [InlineData(">:(", @">:\(")]
    [InlineData("(masterpiece)", "(masterpiece)")]   // 가중치 없는 강조 문법도 사용자가 쓴 그대로
    [InlineData("((best quality))", "((best quality))")]
    [InlineData("evangelion:_3.0_you_can_(not)_redo", @"evangelion:_3.0_you_can_\(not\)_redo")]
    [InlineData(@"piledriver_\(sex\)", @"piledriver_\(sex\)")]   // 이미 이스케이프된 건 두 번 안 함
    [InlineData("(tag:1.2", @"\(tag:1.2")]   // 짝 없는 괄호는 글자로 묶는다(뒤 태그를 삼키지 않게)
    [InlineData("long_hair", "long_hair")]
    public void EscapesLiteralParensOnly(string raw, string expected) =>
        Assert.Equal(expected, TagKey.EscapeLiteralParens(raw));

    [Fact]
    public void GeneratedLineEscapesTagNameParens()
    {
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(FixedRecipe("piledriver_(sex)", "(gloom_(expression):1.1)", "horror_(theme)"),
            NoPools(), new GenerationOptions { UnderscoreToSpace = true }, new FakeRandomSource());
        Assert.Equal(@"piledriver \(sex\), (gloom \(expression\):1.1), horror \(theme\)", line);
    }

    [Fact]
    public void LineStatsStillRecognizesEscapedTags()
    {
        var lookup = new FakeTagLookup().Add("piledriver_(sex)", 1).Add("long_hair", 1);
        var stats = LineStats.Measure(new[] { @"piledriver \(sex\), long hair" }, lookup: lookup);
        Assert.Equal(0, stats.PhraseWordsMax);
        Assert.Equal(2, stats.Max);
    }

    // ── 2-10: 반복 회피 ────────────────────────────────────────

    [Fact]
    public void DuplicateCheckIgnoresResolvedWeightNumbers()
    {
        // 내용은 하나뿐인데 범위 숫자만 줄마다 다르다 → 예전엔 중복으로 안 봐서 경고도 재추첨도 없었다.
        var gen = new WildcardGenerator();
        var result = gen.Generate(FixedRecipe("1girl", "(wide_hips:1.0~1.5)"), NoPools(),
            new GenerationOptions { LineCount = 3, Seed = 5, AvoidDuplicateLines = true });
        Assert.Contains(result.Warnings, w => w.Contains("중복"));
    }

    [Fact]
    public void LineKeyIgnoresWeightAndOrderButNotContent()
    {
        Assert.Equal(WildcardGenerator.LineKey(new[] { "1girl", "(wide_hips:1.1)" }),
                     WildcardGenerator.LineKey(new[] { "(wide_hips:1.35)", "1girl" }));
        Assert.Equal(WildcardGenerator.LineKey(new[] { "smile, implies:happy" }),
                     WildcardGenerator.LineKey(new[] { "smile" }));   // 판정 전용 조각은 출력에 안 나간다
        Assert.NotEqual(WildcardGenerator.LineKey(new[] { "1girl", "wide_hips" }),
                        WildcardGenerator.LineKey(new[] { "1girl", "narrow_waist" }));
    }

    private static Recipe PoolRecipe(int min, int max, params string[] candidates) => new()
    {
        Slots = { new RandomPoolSlot { Label = "그의 손", MinCount = min, MaxCount = max, Tags = new(candidates) } },
    };

    private static List<string> Lines(Recipe recipe, int count, SamplingMode sampling = SamplingMode.Uniform) =>
        new WildcardGenerator().Generate(recipe, NoPools(), new GenerationOptions
        {
            LineCount = count, Seed = 42, AvoidDuplicateLines = false, UnderscoreToSpace = false, Sampling = sampling,
        }).Lines.ToList();

    [Fact]
    public void EqualWeightPoolNeverRepeatsBackToBack()
    {
        var lines = Lines(PoolRecipe(1, 1, "(his hand grips her hip:1.1)", "(his hand presses her breast:1.1)", "(his hand splays across her side:1.1)"), 600);
        for (int i = 1; i < lines.Count; i++) Assert.NotEqual(lines[i - 1], lines[i]);
        // 가중치가 같으면 장기 등장률도 균등하게 유지된다.
        foreach (var g in lines.GroupBy(l => l)) Assert.InRange(g.Count() / 600.0, 0.28, 0.39);
    }

    [Fact]
    public void WeightedPoolKeepsLongRunRatioWhileReducingRepeats()
    {
        // 3::a / 1::b — 명목 75% / 25%. 회피 확률이 편중을 보존하므로 장기 비율은 그대로여야 하고
        // (ALT의 단순 재추첨처럼 75% → 57%로 주저앉으면 안 된다), b는 무게가 최소라 연속으로 안 나온다.
        const int n = 20000;
        var lines = Lines(PoolRecipe(1, 1, "3::alpha", "1::beta"), n);
        double alpha = lines.Count(l => l == "alpha") / (double)n;
        Assert.InRange(alpha, 0.735, 0.765);
        int betaRepeats = 0, alphaRepeats = 0;
        for (int i = 1; i < n; i++)
            if (lines[i] == lines[i - 1]) { if (lines[i] == "beta") betaRepeats++; else alphaRepeats++; }
        Assert.Equal(0, betaRepeats);
        // 회피 없이는 P(a→a) = 0.75, 회피 후 = 0.75 × (1 − 1/9) ≈ 0.667.
        double aa = alphaRepeats / (double)lines.Count(l => l == "alpha");
        Assert.InRange(aa, 0.64, 0.70);
    }

    [Fact]
    public void AvoidanceIsSkippedWhenItCannotApply()
    {
        // 후보 1개: 매 줄 그 후보(회피 불가 → 그냥 뽑는다).
        Assert.All(Lines(PoolRecipe(1, 1, "only_one"), 20), l => Assert.Equal("only_one", l));
        // 전부 뽑는 슬롯(min = max = 후보 수): 매 줄 전부.
        Assert.All(Lines(PoolRecipe(2, 2, "a_tag", "b_tag"), 20), l => Assert.Equal(2, l.Split(", ").Length));
    }

    [Fact]
    public void FrequencyWeightedPoolAlsoAvoidsRepeats()
    {
        // 빈도 가중 추첨(앱 설정에서 켜는 쪽)에서도 동작해야 한다 — 가중치가 거의 늘 달라서 ALT식 "같을 때만" 가드면 꺼진다.
        var lookup = new FakeTagLookup().Add("rare_tag", 100).Add("mid_tag", 400).Add("common_tag", 900);
        var recipe = PoolRecipe(1, 1, "rare_tag", "mid_tag", "common_tag");
        var gen = new WildcardGenerator();
        var lines = gen.Generate(recipe, NoPools(), new GenerationOptions
        {
            LineCount = 6000, Seed = 3, AvoidDuplicateLines = false, UnderscoreToSpace = false, Sampling = SamplingMode.Weighted,
        }, tagInfo: lookup).Lines;
        // 가중치 sqrt: 10 / 20 / 30 → 명목 1/6, 2/6, 3/6. 가장 가벼운 rare_tag는 연속으로 안 나온다.
        for (int i = 1; i < lines.Count; i++)
            Assert.False(lines[i] == "rare_tag" && lines[i - 1] == "rare_tag");
        Assert.InRange(lines.Count(l => l == "common_tag") / 6000.0, 0.47, 0.53);
    }
}
