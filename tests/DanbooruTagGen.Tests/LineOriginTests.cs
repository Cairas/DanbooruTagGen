using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>줄 출처(CaptureOrigins)와 예약 줄(ReservedLineKeys) — 앱의 "줄 출처 보기"·"줄 잠금 후 나머지 다시 뽑기".</summary>
public class LineOriginTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    private static Recipe Sample() => new()
    {
        Slots =
        {
            new FixedSlot { Label = "기본", Tags = { "1girl", "solo" } },
            new AlternativeSlot
            {
                Label = "포즈",
                Groups =
                {
                    new AlternativeGroup { Label = "풀넬슨", Tags = { "full_nelson" }, Weight = 1 },
                    new AlternativeGroup { Label = "", Tags = { "standing_sex" }, Weight = 2 },
                },
            },
            new RandomPoolSlot { Label = "손짓", Tags = { "grabbing_another's_breast", "hand_on_another's_hip", "smile", "blush" }, MinCount = 1, MaxCount = 2 },
        },
    };

    private static readonly ConflictRules HandsRules = new(new[]
    {
        new ConflictGroup("양손 다 씀 vs 손짓", new[] { "full_nelson", "grabbing_another's_breast", "hand_on_another's_hip" }),
    });

    private static GenerationOptions Opts(bool capture, int lines = 200, int seed = 3) => new()
    {
        LineCount = lines, Seed = seed, AvoidDuplicateLines = false, LocalConflictFiltering = true,
        UnderscoreToSpace = false, CaptureOrigins = capture,
    };

    [Fact]
    public void CapturingOriginsDoesNotChangeTheOutput()
    {
        var gen = new WildcardGenerator();
        var off = gen.Generate(Sample(), NoPools(), Opts(false), HandsRules);
        var on = gen.Generate(Sample(), NoPools(), Opts(true), HandsRules);
        Assert.Equal(off.Lines, on.Lines);
        Assert.Null(off.Origins);
        Assert.Equal(on.Lines.Count, on.Origins!.Count);
    }

    [Fact]
    public void OriginsListEverySlotAndAccountForEveryTag()
    {
        var result = new WildcardGenerator().Generate(Sample(), NoPools(), Opts(true), HandsRules);
        for (int i = 0; i < result.Lines.Count; i++)
        {
            var o = result.Origins![i];
            Assert.Equal(new[] { "기본", "포즈", "손짓" }, o.Slots.Select(s => s.SlotLabel));
            Assert.Equal(new[] { SlotOriginKind.Fixed, SlotOriginKind.Alternative, SlotOriginKind.Pool }, o.Slots.Select(s => s.Kind));
            // 슬롯들이 낸 태그를 이으면 그 줄 그대로다(정렬 끔).
            Assert.Equal(result.Lines[i], string.Join(", ", o.Slots.SelectMany(s => s.Emitted)));
            // 라벨 없는 그룹은 "그룹 N"으로 보인다.
            Assert.Contains(o.Slots[1].GroupLabel, new[] { "풀넬슨", "그룹 2" });
        }
    }

    [Fact]
    public void FilteredOutNamesWhatTheLocalFilterRemoved()
    {
        var result = new WildcardGenerator().Generate(Sample(), NoPools(), Opts(true), HandsRules);
        var nelson = Enumerable.Range(0, result.Lines.Count).Where(i => result.Origins![i].Slots[1].GroupLabel == "풀넬슨").ToList();
        Assert.NotEmpty(nelson);
        foreach (var i in nelson)
            Assert.Equal(new[] { "grabbing_another's_breast", "hand_on_another's_hip" },
                result.Origins![i].Slots[2].FilteredOut.OrderBy(x => x, StringComparer.Ordinal));
        // 서서 줄에선 앞쪽 슬롯 때문에 걸러지는 건 없다 — 걸러진 게 있다면 같은 슬롯에서 먼저 뽑은 손짓 때문(2개 뽑기).
        Assert.All(Enumerable.Range(0, result.Lines.Count).Except(nelson), i =>
        {
            var hands = result.Origins![i].Slots[2];
            if (hands.FilteredOut.Count > 0)
                Assert.Contains(hands.Emitted, t => t is "grabbing_another's_breast" or "hand_on_another's_hip");
        });
    }

    [Fact]
    public void FilteredOutNamesExcludedAltGroups()
    {
        // 풀이 먼저 → 손짓이 가슴이면 뒤의 ALT에서 풀넬슨 그룹이 빠진다.
        var recipe = new Recipe
        {
            Slots =
            {
                new RandomPoolSlot { Label = "손짓", Tags = { "grabbing_another's_breast", "smile" } },
                new AlternativeSlot
                {
                    Label = "포즈",
                    Groups =
                    {
                        new AlternativeGroup { Label = "풀넬슨", Tags = { "full_nelson" }, Weight = 1 },
                        new AlternativeGroup { Label = "서서", Tags = { "standing_sex" }, Weight = 2 },
                    },
                },
            },
        };
        var result = new WildcardGenerator().Generate(recipe, NoPools(), Opts(true), HandsRules);
        for (int i = 0; i < result.Lines.Count; i++)
        {
            var o = result.Origins![i];
            if (o.Slots[0].Emitted.Contains("grabbing_another's_breast"))
                Assert.Equal(new[] { "풀넬슨" }, o.Slots[1].FilteredOut);
            else
                Assert.Empty(o.Slots[1].FilteredOut);
        }
    }

    [Fact]
    public void DroppedShowsTagsRemovedByInLineDedupe()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Label = "기본", Tags = { "smile" } },
                new RandomPoolSlot { Label = "표정", Tags = { "smile" } },
            },
        };
        var result = new WildcardGenerator().Generate(recipe, NoPools(), Opts(true, lines: 3) with { DedupeWithinLine = true });
        Assert.All(result.Origins!, o =>
        {
            Assert.Empty(o.Slots[1].Emitted);
            Assert.Equal(new[] { "smile" }, o.Slots[1].Dropped);
        });
    }

    [Fact]
    public void ReservedLinesAreAvoidedLikeAlreadyGeneratedOnes()
    {
        // 서로 다른 줄이 딱 6개(포즈 2 × 표정 3)인 레시피에서 4줄을 예약하면 새로 나올 수 있는 건 2줄뿐.
        var recipe = new Recipe
        {
            Slots =
            {
                new AlternativeSlot { Label = "포즈", Groups = { new AlternativeGroup { Tags = { "standing" } }, new AlternativeGroup { Tags = { "sitting" } } } },
                new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "crying" } },
            },
        };
        var gen = new WildcardGenerator();
        var first = gen.Generate(recipe, NoPools(), new GenerationOptions { LineCount = 4, Seed = 1, UnderscoreToSpace = false });
        Assert.Equal(4, first.LineKeys.Distinct().Count());

        var rest = gen.Generate(recipe, NoPools(), new GenerationOptions
        {
            LineCount = 2, Seed = 2, UnderscoreToSpace = false, ReservedLineKeys = first.LineKeys.ToHashSet(),
        });
        Assert.Empty(rest.Warnings);
        Assert.Empty(rest.LineKeys.Intersect(first.LineKeys));
        Assert.Equal(2, rest.LineKeys.Distinct().Count());
    }

    [Fact]
    public void FormattedOriginNamesSlotsBranchesAndFilteredCandidates()
    {
        var origin = new LineOrigin(new[]
        {
            new SlotOrigin(0, "기본", SlotOriginKind.Fixed, null, new[] { "1girl" }, Array.Empty<string>(), Array.Empty<string>()),
            new SlotOrigin(1, "포즈", SlotOriginKind.Alternative, "풀넬슨", new[] { "full_nelson" }, Array.Empty<string>(), Array.Empty<string>()),
            new SlotOrigin(2, "손짓", SlotOriginKind.Pool, null, new[] { "smile" }, Array.Empty<string>(), new[] { "grabbing_another's_breast" }),
            new SlotOrigin(3, "효과", SlotOriginKind.Pool, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
        }, Retries: 2);
        var text = Core.Output.LineOriginText.Format(origin, new[] { new ConflictHit("시간대", new[] { "day", "night" }) });
        Assert.Contains("[고정] 기본 : 1girl", text);
        Assert.Contains("[대안] 포즈 → 풀넬슨 : full_nelson", text);
        Assert.Contains("후보에서 뺌: grabbing_another's_breast", text);
        Assert.Contains("[랜덤] 효과 : (이번 줄엔 안 나옴)", text);
        Assert.Contains("남은 모순: 시간대(day + night)", text);
    }

    private static Recipe SixCombos() => new()
    {
        Slots =
        {
            new AlternativeSlot { Label = "포즈", Groups = { new AlternativeGroup { Tags = { "standing" } }, new AlternativeGroup { Tags = { "sitting" } } } },
            new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "crying" } },
        },
    };

    [Fact]
    public void StopWhenExhaustedEndsAtTheDistinctLinesWithoutDuplicates()
    {
        var result = new WildcardGenerator().Generate(SixCombos(), NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 4, StopWhenExhausted = true });
        Assert.True(result.Exhausted);
        Assert.Equal(6, result.Lines.Count);
        Assert.Equal(6, result.LineKeys.Distinct().Count());
        Assert.Contains(result.Warnings, w => w.Contains("6줄에서 멈췄습니다(요청 200줄)"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("중복될 수 있습니다"));
    }

    [Fact]
    public void StopWhenExhaustedRespectsReservedLines()
    {
        var gen = new WildcardGenerator();
        var first = gen.Generate(SixCombos(), NoPools(), new GenerationOptions { LineCount = 4, Seed = 1 });
        var rest = gen.Generate(SixCombos(), NoPools(), new GenerationOptions
        {
            LineCount = 50, Seed = 2, StopWhenExhausted = true, ReservedLineKeys = first.LineKeys.ToHashSet(),
        });
        Assert.Equal(2, rest.Lines.Count);
        Assert.Empty(rest.LineKeys.Intersect(first.LineKeys));
    }

    [Fact]
    public void StopWhenExhaustedChangesNothingWhileCombinationsRemain()
    {
        var wide = new Recipe
        {
            Slots =
            {
                new RandomPoolSlot { Tags = new(Enumerable.Range(0, 40).Select(i => $"a{i}")) },
                new RandomPoolSlot { Tags = new(Enumerable.Range(0, 40).Select(i => $"b{i}")) },
            },
        };
        var gen = new WildcardGenerator();
        var off = gen.Generate(wide, NoPools(), new GenerationOptions { LineCount = 300, Seed = 9 });
        var on = gen.Generate(wide, NoPools(), new GenerationOptions { LineCount = 300, Seed = 9, StopWhenExhausted = true });
        Assert.False(on.Exhausted);
        Assert.Equal(off.Lines, on.Lines);
    }

    [Fact]
    public void StopWhenExhaustedNeedsDuplicateAvoidance()
    {
        var result = new WildcardGenerator().Generate(SixCombos(), NoPools(),
            new GenerationOptions { LineCount = 50, Seed = 4, StopWhenExhausted = true, AvoidDuplicateLines = false });
        Assert.False(result.Exhausted);
        Assert.Equal(50, result.Lines.Count);
    }

    [Fact]
    public void ReservedLinesAreIgnoredWhenDuplicateAvoidanceIsOff()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "smile" } } } };
        var gen = new WildcardGenerator();
        var first = gen.Generate(recipe, NoPools(), new GenerationOptions { LineCount = 1, Seed = 1 });
        var again = gen.Generate(recipe, NoPools(), new GenerationOptions
        {
            LineCount = 1, Seed = 1, AvoidDuplicateLines = false, ReservedLineKeys = first.LineKeys.ToHashSet(),
        });
        Assert.Equal(first.Lines, again.Lines);
        Assert.Empty(again.Warnings);
    }
}
