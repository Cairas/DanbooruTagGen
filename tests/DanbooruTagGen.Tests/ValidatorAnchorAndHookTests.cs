using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-5 "닻 없는 자연어"와 1-10(b) 훅 검사.</summary>
public class ValidatorAnchorAndHookTests
{
    private static FakeTagLookup Dict() => new FakeTagLookup()
        .Add("1girl", 1).Add("1boy", 1).Add("sex", 1).Add("smile", 1)
        .Add("grabbing_another's_breast", 1).Add("voyeurism", 1);

    private static Recipe WithPool(params string[] candidates) => new()
    {
        Slots =
        {
            new FixedSlot { Tags = { "1girl", "1boy" } },
            new RandomPoolSlot { Label = "그의 손", Tags = new(candidates) },
        },
    };

    private static IReadOnlyList<ValidationIssue> Anchorless(Recipe r, IEnumerable<GenerationHook>? hooks = null) =>
        RecipeValidator.Validate(r, new Dictionary<string, Pool>(), Dict(), hooks: hooks)
            .Where(i => i.Rule == ValidationRule.AnchorlessPhrase).ToList();

    // ── 1-5 ─────────────────────────────────────────────────────

    [Fact]
    public void FlagsBodyPhraseWithoutCompanionTag()
    {
        // n560 "그의 손" 풀에서 실제로 겪은 형태: 손 동작 문장만 있고 동반 태그가 없다.
        var issue = Assert.Single(Anchorless(WithPool("(his hand presses firmly into her breast as he moves:1.1)", "smile")));
        Assert.Contains("hand", issue.Message);
        Assert.Contains("그의 손", issue.Message);
    }

    [Theory]
    [InlineData("(his hand presses firmly into her breast:1.1), grabbing_another's_breast")]   // 동반 태그
    [InlineData("(his hand presses firmly into her breast:1.1), implies:grabbing_another's_breast")]   // 판정 전용 동반 태그
    [InlineData("(she never thought to take her wedding ring off:1.0)")]   // 신체·자세 낱말 없음
    public void DoesNotFlagAnchoredOrNonBodyPhrases(string candidate) =>
        Assert.Empty(Anchorless(WithPool(candidate)));

    [Fact]
    public void MisspelledCompanionIsNotAnAnchor()
    {
        // 사전에 없는 태그는 충돌 엔진의 규칙과도 안 맞으므로 닻이 아니다(존재 검사도 따로 걸린다).
        Assert.Single(Anchorless(WithPool("(his hand grips her:1.1), grabing_another's_breast")));
    }

    [Fact]
    public void AltGroupIsOneUnitAndFixedSlotsAreNotChecked()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Tags = { "1girl", "(kneeling with her hands bound:1.1)" } },   // 고정은 대상 아님
                new AlternativeSlot
                {
                    Label = "단계",
                    Groups =
                    {
                        new AlternativeGroup { Label = "닻 있음", Tags = { "(his hands pin her wrists:1.1)", "sex", "1boy" } },
                        new AlternativeGroup { Label = "닻 없음", Tags = { "(bent over the table, his hand on her back:1.1)" } },
                        new AlternativeGroup { Label = "안 뽑힘", Weight = 0, Tags = { "(his hand on her neck:1.1)" } },
                    },
                },
            },
        };
        var issue = Assert.Single(Anchorless(recipe));
        Assert.Contains("닻 없음", issue.Message);
    }

    [Fact]
    public void PhraseThatIsItselfAConflictMemberIsAnchored()
    {
        // conflicts.csv에 자연어 멤버로 올라 있는 문장은 엔진이 이미 본다(n516 "입 벌려 검사").
        var phrase = "(his hand gripping her jaw, prying her mouth open:1.2)";
        var rules = new ConflictRules(new[] { new ConflictGroup("입마개 vs 입 벌려 검사", new[] { "bit_gag", phrase }) });
        var recipe = WithPool(phrase);
        Assert.Single(Anchorless(recipe));
        Assert.DoesNotContain(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict(), conflicts: rules),
            i => i.Rule == ValidationRule.AnchorlessPhrase);
    }

    [Fact]
    public void LocationOnlyBodyWordsAreNotFlagged() =>
        // 문신 위치·땀 묘사처럼 동작이 아닌 위치 낱말(hip, thigh, knees)은 소음이라 잡지 않는다.
        Assert.Empty(Anchorless(WithPool("(a fierce tiger tattoo spans her hip:1.1)", "(the backs of her knees shine with sweat:1.0)")));

    [Fact]
    public void ZeroPickWeightCandidateIsNotChecked() =>
        Assert.Empty(Anchorless(WithPool("0::(his hand presses into her breast:1.1)", "smile")));

    // ── 1-10(b) 훅 ──────────────────────────────────────────────

    [Fact]
    public void EnabledHookSlotsAreValidatedAndLabeled()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl" } } } };
        var hooks = new[]
        {
            new GenerationHook { Name = "광원", Kind = HookSlotKind.Random, Tags = { "rim_lightt", "smile" } },
        };
        var issues = RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict(), hooks: hooks);
        var issue = Assert.Single(issues);
        Assert.Equal(ValidationRule.UnknownTag, issue.Rule);
        Assert.Equal("rim_lightt", issue.Subject);
        Assert.Contains("(훅 '광원')", issue.Message);
        Assert.Single(recipe.Slots);   // 원본 레시피는 그대로
    }

    [Fact]
    public void DisabledHooksAreNotValidated()
    {
        // 꺼진 훅은 생성(HookApplier)에서도 빠지므로 검사 대상이 아니다.
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl" } } } };
        var hooks = new[] { new GenerationHook { Name = "꺼둠", IsEnabled = false, Tags = { "made_up_tag" } } };
        Assert.Empty(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict(), hooks: hooks));
    }

    [Fact]
    public void HookCanSupplyThePartner()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "sex" } } } };
        Assert.Single(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict()));   // 상대 없음
        var hooks = new[] { new GenerationHook { Name = "상대", Kind = HookSlotKind.Fixed, Tags = { "1boy" } } };
        Assert.Empty(RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict(), hooks: hooks));
    }

    [Fact]
    public void AnchorlessPhraseInHookPoolIsReportedWithHookName()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "1boy" } } } };
        var hooks = new[] { new GenerationHook { Name = "손", Kind = HookSlotKind.Random, Tags = { "(his hand on her hip:1.1)" } } };
        var issue = Assert.Single(Anchorless(recipe, hooks));
        Assert.Contains("(훅 '손')", issue.Message);
    }
}
