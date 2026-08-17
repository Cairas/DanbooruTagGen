using System.Text;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>Anima 출력 모드. 이 모델은 Qwen LLM을 텍스트 인코더로 써서 자연어를 잘 읽는 대신
/// 태그 과밀에 민감하다 — COSMETIC 축을 버리고 서술문을 붙이는 게 핵심 동작이다.</summary>
public class AnimaFormatTests
{
    private static IReadOnlyDictionary<string, Pool> NoPools() => new Dictionary<string, Pool>();

    private static AnimaPhraseBook Book(params (string Tag, string Phrase)[] rows)
        => new(rows.ToDictionary(r => r.Tag, r => r.Phrase, StringComparer.Ordinal));

    // ── 슬롯 역할 판정 ──────────────────────────────────────────

    [Fact]
    public void FixedSlotIsIdentity()
        => Assert.Equal(SlotRole.Identity, SlotRoleClassifier.Classify(new FixedSlot { Label = "기본" }));

    [Theory]
    [InlineData("체위", SlotRole.Major)]
    [InlineData("배경", SlotRole.Major)]
    [InlineData("옷 상태", SlotRole.Major)]
    [InlineData("표정", SlotRole.Minor)]
    [InlineData("구도", SlotRole.Cosmetic)]
    [InlineData("조명", SlotRole.Cosmetic)]
    [InlineData("가슴·유두 디테일", SlotRole.Cosmetic)]
    public void LabelDecidesRole(string label, SlotRole expected)
        => Assert.Equal(expected, SlotRoleClassifier.Classify(new RandomPoolSlot { Label = label }));

    [Fact]
    public void UnknownLabelIsReportedNotGuessed()
        => Assert.Equal(SlotRole.Unknown, SlotRoleClassifier.Classify(new RandomPoolSlot { Label = "듣도보도 못한 축" }));

    [Fact]
    public void AltWithDifferentVisualTagsIsMajorEvenIfLabelSaysOtherwise()
    {
        // 인질극의 "심리 상태" ALT는 이름과 달리 그룹마다 sex/after_sex를 품고 있어
        // 사실상 행위 진행 축이다. 라벨만 보면 MINOR로 잘못 세게 된다.
        var alt = new AlternativeSlot
        {
            Label = "심리 상태",
            Groups =
            {
                new AlternativeGroup { Tags = { "scared", "trembling" } },
                new AlternativeGroup { Tags = { "sex", "vaginal" } },
                new AlternativeGroup { Tags = { "after_sex" } },
            },
        };
        Assert.Equal(SlotRole.Major, SlotRoleClassifier.Classify(alt));
    }

    // ── 조각 사전 로드 ──────────────────────────────────────────

    [Fact]
    public void LoadsCsvSkippingCommentsHeaderAndHandlingQuotes()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join("\n",
            "# 주석",
            "tag,phrase",
            "on_back,laid out on her back",
            "bedroom,\"in a bedroom, dimly lit\"",
            "hetero,-",
            ""), new UTF8Encoding(false));
        try
        {
            var book = AnimaPhraseBook.LoadFromFile(path);

            Assert.True(book.TryGet("on_back", out var back));
            Assert.Equal("laid out on her back", back);
            Assert.True(book.TryGet("bedroom", out var bed));
            Assert.Equal("in a bedroom, dimly lit", bed);   // 큰따옴표 안의 쉼표 보존
            // "-"는 "서술로 옮길 게 없어 일부러 비워 둔" 표시라 조각이 없는 것으로 취급한다.
            Assert.False(book.TryGet("hetero", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFileGivesEmptyBookInsteadOfThrowing()
        => Assert.Equal(0, AnimaPhraseBook.LoadFromFile(Path.Combine(Path.GetTempPath(), "no-such-file.csv")).Count);

    // ── 렌더링 ─────────────────────────────────────────────────

    [Fact]
    public void RenderDropsCosmeticTagsAndAppendsSentence()
    {
        var parts = new List<(string, SlotRole)>
        {
            ("1girl", SlotRole.Identity),
            ("on_back", SlotRole.Major),
            ("from_below", SlotRole.Cosmetic),   // 구도 — Anima에서는 버린다
            ("tears", SlotRole.Minor),
        };
        var book = Book(("1girl", "a young woman"), ("on_back", "laid out on her back"),
                        ("tears", "tears on her face"), ("from_below", "seen from below"));

        var line = book.Render(parts, underscoreToSpace: false);

        Assert.DoesNotContain("from_below", line);
        Assert.DoesNotContain("seen from below", line);   // 버린 태그는 문장에도 안 들어간다
        Assert.StartsWith("1girl, on_back, tears. ", line);
        Assert.EndsWith("A young woman, laid out on her back, tears on her face.", line);
    }

    [Fact]
    public void NaturalLanguagePhraseGoesOnlyIntoTheSentence()
    {
        // 자연어 문구를 태그 줄에도 넣으면 같은 내용이 두 번 나가 길이만 먹는다.
        var parts = new List<(string, SlotRole)>
        {
            ("1girl", SlotRole.Identity),
            ("taken hostage by a masked intruder", SlotRole.Identity),
        };
        var line = Book(("1girl", "a young woman")).Render(parts, underscoreToSpace: false);

        Assert.Equal("1girl. A young woman, taken hostage by a masked intruder.", line);
    }

    [Fact]
    public void TagsWithoutPhraseAreSkippedNotBroken()
    {
        var parts = new List<(string, SlotRole)> { ("1girl", SlotRole.Identity), ("mystery_tag", SlotRole.Major) };
        var line = Book(("1girl", "a young woman")).Render(parts, underscoreToSpace: false);

        Assert.Contains("mystery_tag", line);                    // 태그 줄에는 남고
        Assert.EndsWith("A young woman.", line);                 // 문장에서만 빠진다
    }

    [Fact]
    public void DuplicatePhrasesAppearOnce()
    {
        // sex와 vaginal을 같은 문구로 매핑하는 건 의도된 것 — 문장에는 한 번만 나와야 한다.
        var parts = new List<(string, SlotRole)> { ("sex", SlotRole.Major), ("vaginal", SlotRole.Major) };
        var line = Book(("sex", "being taken"), ("vaginal", "being taken")).Render(parts, false);

        Assert.Equal("sex, vaginal. Being taken.", line);
    }

    [Fact]
    public void SymbolOnlyTagKeepsItsUnderscore()
    {
        // @_@(어질어질한 눈)를 "@ @"로 바꾸면 뜻을 잃는다. 글자가 없는 태그는 변환하지 않는다.
        Assert.Equal("@_@", AnimaPhraseBook.ToDisplay("@_@"));
        Assert.Equal("on back", AnimaPhraseBook.ToDisplay("on_back"));
    }

    // ── 생성기 통합 ────────────────────────────────────────────

    [Fact]
    public void TagsFormatIsUnchangedByDefault()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Label = "기본", Tags = { "1girl", "on_back" } } } };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 1, UnderscoreToSpace = false });

        Assert.Equal("1girl, on_back", result.Lines[0]);
    }

    [Fact]
    public void AnimaFormatUsesPhraseBookEndToEnd()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Label = "기본", Tags = { "1girl" } },
                new RandomPoolSlot { Label = "체위", Tags = { "on_back" }, MinCount = 1, MaxCount = 1 },
                new RandomPoolSlot { Label = "구도", Tags = { "from_below" }, MinCount = 1, MaxCount = 1 },
            },
        };
        var book = Book(("1girl", "a young woman"), ("on_back", "laid out on her back"));

        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 1, UnderscoreToSpace = false, Format = PromptFormat.Anima },
            conflicts: null, tagInfo: null, phrases: book);

        Assert.Equal("1girl, on_back. A young woman, laid out on her back.", result.Lines[0]);
    }
}
