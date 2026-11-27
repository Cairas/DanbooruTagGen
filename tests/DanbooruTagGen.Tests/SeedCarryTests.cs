using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>시드 칸이 자동일 때: 미리보기에서 본 줄이 바로 다음 [생성]의 파일 앞쪽에 그대로 들어가야 한다.</summary>
public class SeedCarryTests
{
    private static Func<int> Rolls(params int[] values)
    {
        var queue = new Queue<int>(values);
        return () => queue.Dequeue();
    }

    [Fact]
    public void GenerateRightAfterPreviewReusesPreviewSeedOnce()
    {
        var carry = new SeedCarry();
        var roll = Rolls(11, 22, 33);
        Assert.Equal((11, false), carry.Next(fixedSeed: null, preview: true, roll));
        Assert.Equal((11, true), carry.Next(null, preview: false, roll));    // 이어 씀
        carry.Written();
        Assert.Equal((22, false), carry.Next(null, preview: false, roll));   // 파일에 쓴 뒤엔 한 번으로 끝
    }

    /// <summary>생성이 쓰기 전에 멈추면(새 파일 모드 충돌·취소·검증 오류) 다음 생성이 여전히 미리보기 시드를 이어 쓴다.</summary>
    [Fact]
    public void GenerateThatDidNotWriteKeepsTheCarry()
    {
        var carry = new SeedCarry();
        var roll = Rolls(7, 8);
        carry.Next(null, true, roll);
        Assert.Equal((7, true), carry.Next(null, false, roll));
        Assert.Equal((7, true), carry.Next(null, false, roll));
    }

    [Fact]
    public void EachPreviewRerollsAndTheLastOneIsCarried()
    {
        var carry = new SeedCarry();
        var roll = Rolls(1, 2);
        carry.Next(null, true, roll);
        carry.Next(null, true, roll);
        Assert.Equal((2, true), carry.Next(null, false, roll));
    }

    [Fact]
    public void FixedSeedOrResetDropsTheCarry()
    {
        var carry = new SeedCarry();
        var roll = Rolls(5, 6, 7);
        carry.Next(null, true, roll);
        Assert.Equal((99, false), carry.Next(99, false, roll));   // 시드 칸에 숫자를 넣었으면 그 숫자
        Assert.Equal((6, false), carry.Next(null, false, roll));  // 고정 시드를 쓰는 순간 이어 쓰기는 사라진다

        carry.Next(null, true, roll);
        carry.Reset();                                           // 시드 칸을 바꿨다
        Assert.False(carry.Next(null, false, () => 8).Carried);
    }

    /// <summary>이어 쓴 시드면 미리보기 줄이 생성 결과 맨 앞에 그대로 온다(생성기의 앞부분 일치 성질).</summary>
    [Fact]
    public void CarriedSeedPutsPreviewLinesAtTheFrontOfTheFile()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "crying", "angry", "pout", "smirk" }, MinCount = 1, MaxCount = 1 },
                new RandomPoolSlot { Label = "자세", Tags = { "standing", "sitting", "kneeling", "lying", "squatting" }, MinCount = 1, MaxCount = 1 },
            },
        };
        var carry = new SeedCarry();
        var roll = Rolls(424242);
        var pools = new Dictionary<string, Pool>();
        var gen = new WildcardGenerator();
        var preview = gen.Generate(recipe, pools, new GenerationOptions { LineCount = 3, Seed = carry.Next(null, true, roll).Seed });
        var file = gen.Generate(recipe, pools, new GenerationOptions { LineCount = 20, Seed = carry.Next(null, false, roll).Seed });
        Assert.Equal(preview.Lines, file.Lines.Take(3));
    }
}
