using System.Text.Json;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>RandomPoolSlot.ChancePercent — 개수(Min~Max)와 별개로 "이 슬롯이 아예 뜰 확률".</summary>
public class ChancePercentTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    /// <summary>chancePercent 키가 없는 기존 레시피 JSON(수백 개가 전부 이 모양).</summary>
    private const string LegacyJson = """
        {
          "id": "legacy",
          "name": "legacy",
          "slots": [
            { "type": "fixed", "label": "기본", "tags": ["1girl"] },
            { "type": "randomPool", "label": "표정", "tags": ["a", "b", "c", "d"], "minCount": 1, "maxCount": 3 }
          ]
        }
        """;

    private static Recipe Load(string json) => JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;

    private static Recipe WithChance(int chance, int min = 1, int max = 3) => new()
    {
        Slots =
        {
            new FixedSlot { Label = "기본", Tags = { "1girl" } },
            new RandomPoolSlot { Label = "표정", Tags = { "a", "b", "c", "d", "e", "f" }, MinCount = min, MaxCount = max, ChancePercent = chance },
        },
    };

    private static GenerationOptions Many(int lines, int? seed = null) =>
        new() { LineCount = lines, Seed = seed, UnderscoreToSpace = false, AvoidDuplicateLines = false };

    /// <summary>"1girl" 뒤에 붙은 랜덤 슬롯 태그 수.</summary>
    private static int RandomCount(string line) => line.Split(", ").Length - 1;

    [Fact]
    public void LegacyJsonDefaultsToAlways()
    {
        var slot = Assert.IsType<RandomPoolSlot>(Load(LegacyJson).Slots[1]);
        Assert.Equal(100, slot.ChancePercent);
        WildcardGenerator.Validate(Load(LegacyJson), NoPools()); // no throw
    }

    /// <summary>기존 로직으로 손계산한 결과와 정확히 같아야 한다: count = 1 + Next(3)=2 → 3개,
    /// 그다음 비복원 추첨 1→b, 0→a, 0→c. 발동 굴림이 난수를 하나라도 먹으면 전부 밀려 달라진다.</summary>
    [Fact]
    public void LegacyRecipeProducesExactlyThePreFeatureLine()
    {
        var line = new WildcardGenerator().GenerateLine(Load(LegacyJson), NoPools(),
            new GenerationOptions { UnderscoreToSpace = false }, new FakeRandomSource(2, 1, 0, 0));
        Assert.Equal("1girl, b, a, c", line);
    }

    [Fact]
    public void AlwaysChanceConsumesNoExtraRandomness()
    {
        var withField = WithChance(100);
        withField.Slots[1] = new RandomPoolSlot { Label = "표정", Tags = { "a", "b", "c", "d", "e", "f" }, MinCount = 1, MaxCount = 3 };
        var r1 = new WildcardGenerator().Generate(withField, NoPools(), Many(300, 9));
        var r2 = new WildcardGenerator().Generate(WithChance(100), NoPools(), Many(300, 9));
        Assert.Equal(r1.Lines, r2.Lines);
    }

    [Fact]
    public void ZeroChanceNeverFires()
    {
        var result = new WildcardGenerator().Generate(WithChance(0, min: 2, max: 4), NoPools(), Many(500, 1));
        Assert.All(result.Lines, l => Assert.Equal("1girl", l));
    }

    [Fact]
    public void FullChanceAlwaysYieldsMinToMax()
    {
        var result = new WildcardGenerator().Generate(WithChance(100, min: 1, max: 3), NoPools(), Many(500, 2));
        Assert.All(result.Lines, l => Assert.InRange(RandomCount(l), 1, 3));
    }

    [Fact]
    public void HalfChanceFiresAboutHalfTheTime()
    {
        var result = new WildcardGenerator().Generate(WithChance(50, min: 1, max: 6), NoPools(), Many(4000));
        double empty = result.Lines.Count(l => RandomCount(l) == 0) / (double)result.Lines.Count;
        // 기대 50%. 난수 흔들림을 감안해 넉넉한 구간으로 본다(회귀만 잡으면 됨).
        Assert.InRange(empty, 0.45, 0.55);
        // 발동한 줄은 MinCount(1) 이상 — 발동 여부와 개수가 독립이라는 게 이 기능의 요점.
        Assert.All(result.Lines.Where(l => RandomCount(l) > 0), l => Assert.InRange(RandomCount(l), 1, 6));
    }

    [Fact]
    public void SameSeedReproducesIdenticalLines()
    {
        var gen = new WildcardGenerator();
        var r1 = gen.Generate(WithChance(70), NoPools(), Many(200, 42));
        var r2 = gen.Generate(WithChance(70), NoPools(), Many(200, 42));
        Assert.Equal(r1.Lines, r2.Lines);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(RandomPoolSlot.UnparsedChance)]
    public void ValidateRejectsOutOfRange(int chance)
    {
        var ex = Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(WithChance(chance), NoPools()));
        Assert.Contains("발동 확률", ex.Message);
    }

    /// <summary>Weight 소수 사고의 재발 방지: 정수가 아닌 값이 들어와도 파일 로드가 깨지면
    /// 안 되고(그러면 레시피가 조용히 사라진다), 대신 생성 전 검증에서 명시적으로 걸려야 한다.</summary>
    [Theory]
    [InlineData("70.5")]
    [InlineData("\"70\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[70]")]
    public void NonIntegerJsonLoadsButFailsValidation(string raw)
    {
        var json = LegacyJson.Replace("\"maxCount\": 3", $"\"maxCount\": 3, \"chancePercent\": {raw}");
        var recipe = Load(json);
        Assert.Equal(RandomPoolSlot.UnparsedChance, ((RandomPoolSlot)recipe.Slots[1]).ChancePercent);
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, NoPools()));
    }

    [Fact]
    public void JsonRoundTripsAsIntegerWithCamelCaseKey()
    {
        var json = JsonSerializer.Serialize(WithChance(70), JsonStore.Options);
        Assert.Contains("\"chancePercent\": 70", json);
        Assert.Equal(70, ((RandomPoolSlot)Load(json).Slots[1]).ChancePercent);
    }

    [Fact]
    public void EditorTextBlankMeansAlwaysAndBadTextShowsError()
    {
        var slot = new RandomPoolSlot { ChancePercent = 40 };

        slot.ChancePercentText = "";
        Assert.Equal(100, slot.ChancePercent);
        Assert.False(slot.HasChancePercentError);

        slot.ChancePercentText = "abc";
        Assert.Equal(100, slot.ChancePercent); // 잘못된 글자는 값을 바꾸지 않는다
        Assert.True(slot.HasChancePercentError);

        slot.ChancePercentText = "150";
        Assert.Equal(150, slot.ChancePercent); // 넣어 두어 생성 검증도 같이 막는다
        Assert.True(slot.HasChancePercentError);

        slot.ChancePercentText = "70";
        Assert.Equal(70, slot.ChancePercent);
        Assert.False(slot.HasChancePercentError);
    }

    [Fact]
    public void VarietyCountsTheEmptyOutcome()
    {
        // 후보 6개, 1~1개: 항상이면 6가지, 가끔이면 "0개"가 하나 더, 0%면 1가지.
        Assert.Equal(6, VarietyAnalyzer.SlotCardinality(WithChance(100, 1, 1).Slots[1], NoPools()));
        Assert.Equal(7, VarietyAnalyzer.SlotCardinality(WithChance(70, 1, 1).Slots[1], NoPools()));
        Assert.Equal(1, VarietyAnalyzer.SlotCardinality(WithChance(0, 1, 1).Slots[1], NoPools()));
    }
}
