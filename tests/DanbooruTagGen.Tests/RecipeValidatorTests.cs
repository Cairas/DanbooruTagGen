using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>레시피를 저장하기 전에 기계적으로 잡을 수 있는 실수를 점검한다.
/// 지금까지 이 검사는 tools/*.py로만 돌 수 있어서, 앱만 쓰는 흐름에서는 존재하지 않는
/// 태그가 팩에 섞여 들어가도 아무도 몰랐다(실제로 8개 팩에 3건이 새어 들어간 적 있다).</summary>
public class RecipeValidatorTests
{
    private static FakeTagLookup Dict() => new FakeTagLookup()
        .Add("1girl", 5_000_000)
        .Add("1boy", 900_000)
        .Add("fellatio", 200_000)
        .Add("huge_penis", 90_000)
        .Add("averting_eyes", 40_000)
        .Add("tentacles", 120_000)
        .Add("solo", 4_000_000)
        .Add("cum_in_pussy", 300_000)
        .Add("after_fellatio", 20_000)
        .Add("cum_in_mouth", 150_000);

    private static Recipe RecipeWith(params Slot[] slots) => new() { Name = "테스트", Slots = slots.ToList() };

    private static IReadOnlyList<ValidationIssue> Validate(Recipe r, ITagLookup dict, params Pool[] pools)
        => RecipeValidator.Validate(r, pools.ToDictionary(p => p.Id), dict);

    // ── 규칙 1: 존재하지 않는 태그 ────────────────────────────────────────

    [Fact]
    public void ReportsTagThatIsNotInTheDictionary()
    {
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "trembling_hands" } });

        var issues = Validate(recipe, Dict());

        var issue = Assert.Single(issues);
        Assert.Equal(ValidationRule.UnknownTag, issue.Rule);
        Assert.Equal("trembling_hands", issue.Subject);
    }

    [Fact]
    public void AcceptsTagWrittenWithWeightSyntax()
    {
        // 레시피에는 "(huge_penis:1.2)" 형태로 저장돼 있다 — 가중치를 벗기고 봐야 한다.
        var recipe = RecipeWith(new FixedSlot { Tags = { "(huge_penis:1.2)" } });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void SkipsAuthoredPhrasesThatContainSpaces()
    {
        // 실제 태그는 전부 언더스코어로 저장된다. 공백이 있으면 작성자가 일부러 쓴 표현이므로
        // 사전에 없다고 오탈자로 몰면 안 된다("pink smoke"처럼 짧은 것도 의도된 표현이다).
        var recipe = RecipeWith(new FixedSlot
        {
            Tags = { "pink smoke", "she is dragged into the facility, still in shock" },
        });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void IncludesDictionarySuggestionsForAnUnknownTag()
    {
        // 오탈자는 대개 진짜 태그와 몇 글자만 다르다. 사전 검색을 주입받아 "혹시 이것?"을
        // 메시지에 붙인다(검색 자체는 TagDatabase 몫이라 여기선 호출 여부만 검증).
        var recipe = RecipeWith(new FixedSlot { Tags = { "stomache_bulge" } });

        var issues = RecipeValidator.Validate(recipe, new Dictionary<string, Pool>(), Dict(),
            suggest: q => q.StartsWith("stomach") ? new[] { "stomach_bulge" } : Array.Empty<string>());

        Assert.Contains("stomach_bulge", Assert.Single(issues).Message);
    }

    [Fact]
    public void ChecksTagsInsideAlternativeGroupsAndPools()
    {
        var alt = new AlternativeSlot { Label = "진행" };
        alt.Groups.Add(new AlternativeGroup { Tags = { "made_up_tag" } });
        var pool = new Pool { Id = "p1", Name = "축", Candidates = { "another_fake_tag" } };
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl" } }, alt, new RandomPoolSlot { PoolId = "p1" });

        var subjects = Validate(recipe, Dict(), pool).Select(i => i.Subject).ToList();

        Assert.Contains("made_up_tag", subjects);
        Assert.Contains("another_fake_tag", subjects);
    }

    // ── 규칙 2: 상대가 필요한 행위인데 상대가 없음 ──────────────────────

    [Fact]
    public void ReportsActThatNeedsAPartnerWhenNoPartnerIsPresent()
    {
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "fellatio" } });

        var issue = Assert.Single(Validate(recipe, Dict()));
        Assert.Equal(ValidationRule.MissingPartner, issue.Rule);
        Assert.Equal("fellatio", issue.Subject);
    }

    [Fact]
    public void AcceptsPartnerGivenAsATag()
    {
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "1boy", "fellatio" } });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void AcceptsNonHumanPartner()
    {
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "tentacles", "fellatio" } });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void AcceptsPartnerDescribedOnlyInAnAuthoredPhrase()
    {
        // 상대를 태그가 아니라 자연어 문장으로만 세우는 팩이 실제로 있다 —
        // 그걸 "상대 없음"으로 지적하면 검사가 못 쓰게 된다.
        var recipe = RecipeWith(new FixedSlot
        {
            Tags = { "1girl", "fellatio", "a man holds her head down while she is used" },
        });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void CleanRecipeHasNoIssues()
    {
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "1boy", "fellatio", "(huge_penis:1.2)" } });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void AcceptsAftermathTagsWithNobodyElseInFrame()
    {
        // 행위가 끝난 뒤를 그리는 컨셉은 상대가 화면에 없는 게 정상이다 — 몸에 남은 흔적은
        // 상대가 떠난 뒤에도 남는 "상태"라서 상대 마커를 요구하면 안 된다.
        // (실제 팩 3개가 이 이유로 잘못 지적됐다: 사후 정액 노출 컨셉들)
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "solo", "cum_in_pussy" } });

        Assert.Empty(Validate(recipe, Dict()));
    }

    [Fact]
    public void StillReportsAnInProgressActEvenWithAftermathTags()
    {
        // 반대로 "진행 중" 행위는 상대가 지금 화면에 있어야 한다.
        var recipe = RecipeWith(new FixedSlot { Tags = { "1girl", "cum_in_mouth", "fellatio" } });

        var issue = Assert.Single(Validate(recipe, Dict()));
        Assert.Equal("fellatio", issue.Subject);
    }
}
