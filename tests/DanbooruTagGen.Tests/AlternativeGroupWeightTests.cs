using System.Text.Json;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>대안 그룹 가중치(AlternativeGroup.Weight) 동작.
/// 도입 배경: 가중치가 없던 시절엔 특정 상태를 더 자주 뽑으려면 같은 그룹을 복제하는 수밖에
/// 없었고, 그 탓에 ALT의 그룹 칸이 장면 다양성 대신 비중 조절에 낭비됐다.</summary>
public class AlternativeGroupWeightTests
{
    private static IReadOnlyDictionary<string, Pool> NoPools() => new Dictionary<string, Pool>();

    private static Recipe TwoGroups(int weightA, int weightB) => new()
    {
        Slots =
        {
            new AlternativeSlot
            {
                Groups =
                {
                    new AlternativeGroup { Label = "A", Tags = { "a" }, Weight = weightA },
                    new AlternativeGroup { Label = "B", Tags = { "b" }, Weight = weightB },
                },
            },
        },
    };

    [Fact]
    public void DefaultWeightIsOneAndKeepsUniformBehaviour()
    {
        var recipe = TwoGroups(1, 1);
        var gen = new WildcardGenerator();
        var opts = new GenerationOptions { UnderscoreToSpace = false };

        // total=2 이므로 눈금 0 -> A, 1 -> B (가중치 도입 전과 동일한 결과).
        Assert.Equal("a", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(0)));
        Assert.Equal("b", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(1)));
    }

    [Fact]
    public void HigherWeightClaimsProportionallyMoreOfTheRollRange()
    {
        var recipe = TwoGroups(3, 1); // total=4 -> 눈금 0,1,2 = A / 3 = B
        var gen = new WildcardGenerator();
        var opts = new GenerationOptions { UnderscoreToSpace = false };

        Assert.Equal("a", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(0)));
        Assert.Equal("a", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(1)));
        Assert.Equal("a", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(2)));
        Assert.Equal("b", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(3)));
    }

    [Fact]
    public void ZeroWeightGroupIsNeverPicked()
    {
        var recipe = TwoGroups(0, 1);
        var gen = new WildcardGenerator();
        var opts = new GenerationOptions { UnderscoreToSpace = false };

        // total=1 이라 어떤 눈금이든 B만 나온다.
        for (int roll = 0; roll < 5; roll++)
            Assert.Equal("b", gen.GenerateLine(recipe, NoPools(), opts, new FakeRandomSource(roll)));
    }

    [Fact]
    public void WeightDistributionMatchesRatioOverManyLines()
    {
        var recipe = TwoGroups(4, 1);
        var gen = new WildcardGenerator();
        var result = gen.Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 2000, UnderscoreToSpace = false, AvoidDuplicateLines = false });

        int a = result.Lines.Count(l => l == "a");
        // 기대 80%. 난수 흔들림을 감안해 넉넉한 구간으로 본다(회귀만 잡으면 됨).
        Assert.InRange(a / (double)result.Lines.Count, 0.75, 0.85);
    }

    [Fact]
    public void AllZeroWeightsThrows()
    {
        var recipe = TwoGroups(0, 0);
        var ex = Assert.Throws<GenerationValidationException>(
            () => WildcardGenerator.Validate(recipe, NoPools()));
        Assert.Contains("가중치", ex.Message);
    }

    [Fact]
    public void NegativeWeightThrows()
    {
        var recipe = TwoGroups(-1, 1);
        var ex = Assert.Throws<GenerationValidationException>(
            () => WildcardGenerator.Validate(recipe, NoPools()));
        Assert.Contains("음수", ex.Message);
    }

    [Fact]
    public void WeightRoundTripsThroughJson()
    {
        var recipe = TwoGroups(5, 2);
        // 실제 저장 경로와 같은 설정(camelCase)으로 검증해야 의미가 있다.
        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        var back = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;

        var alt = Assert.IsType<AlternativeSlot>(back.Slots[0]);
        Assert.Equal(5, alt.Groups[0].Weight);
        Assert.Equal(2, alt.Groups[1].Weight);
    }

    [Fact]
    public void MissingWeightInJsonDefaultsToOne()
    {
        // 가중치 필드가 없던 시절의 기존 레시피 JSON이 그대로 읽혀야 한다(하위 호환).
        const string json = """
        {"slots":[{"type":"alternative","label":"진행","isEnabled":true,
          "groups":[{"label":"A","tags":["a"]},{"label":"B","tags":["b"]}]}]}
        """;
        var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;
        var alt = Assert.IsType<AlternativeSlot>(recipe.Slots[0]);

        Assert.Equal(1, alt.Groups[0].Weight);
        Assert.Equal(1, alt.Groups[1].Weight);
    }
}
