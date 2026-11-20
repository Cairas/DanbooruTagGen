using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-2: 줄 전체 재추첨 대신(먼저) 슬롯 단위로 후보를 걸러내기.
/// 재추첨은 충돌과 무관한 슬롯까지 다시 굴려서, 충돌 멤버를 품은 ALT 브랜치를 명목 가중치보다 훨씬 덜
/// 나오게 만든다. 로컬 필터는 앞쪽 슬롯의 선택을 존중하고 뒤쪽 슬롯만 맞춘다.</summary>
public class LocalConflictFilteringTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    private static readonly ConflictRules HandsRules = new(new[]
    {
        new ConflictGroup("양손 다 씀 vs 손짓", new[] { "full_nelson", "grabbing_another's_breast", "hand_on_another's_hip" }),
    });

    /// <summary>포즈 ALT(풀넬슨 1 / 서서 2) → 손짓 풀(가슴 / 엉덩이 / 미소). 풀넬슨이면 손짓 셋 중 둘이 충돌.
    /// 가중치를 1:2로 다르게 둔 이유: 가중치가 같으면 ALT의 "직전 그룹 반복 회피"가 켜져 분포 계산이 흐려진다.</summary>
    private static Recipe PoseThenHands() => new()
    {
        Slots =
        {
            new AlternativeSlot
            {
                Label = "포즈",
                Groups =
                {
                    new AlternativeGroup { Label = "풀넬슨", Tags = { "full_nelson" }, Weight = 1 },
                    new AlternativeGroup { Label = "서서", Tags = { "standing_sex" }, Weight = 2 },
                },
            },
            new RandomPoolSlot { Label = "손짓", Tags = { "grabbing_another's_breast", "hand_on_another's_hip", "smile" } },
        },
    };

    private static double FullNelsonShare(bool local, int seed = 11)
    {
        var result = new WildcardGenerator().Generate(PoseThenHands(), NoPools(),
            new GenerationOptions { LineCount = 4000, Seed = seed, AvoidDuplicateLines = false, LocalConflictFiltering = local },
            HandsRules);
        Assert.Empty(result.Conflicts);
        return result.Lines.Count(l => l.Contains("full nelson")) / (double)result.Lines.Count;
    }

    [Fact]
    public void WholeLineRerollSuppressesTheConflictingBranch()
    {
        // 재추첨만: P(풀넬슨 채택) = (⅓·⅓) / (⅓·⅓ + ⅔) = 1/7 ≈ 14.3% — 명목 33.3%가 반 넘게 깎인다.
        Assert.InRange(FullNelsonShare(local: false), 0.125, 0.16);
    }

    [Fact]
    public void LocalFilteringKeepsTheBranchAtItsNominalWeight()
    {
        Assert.InRange(FullNelsonShare(local: true), 0.31, 0.36);
    }

    [Fact]
    public void LocalFilteringPicksOnlyCompatibleCandidatesAfterTheEarlierSlot()
    {
        var result = new WildcardGenerator().Generate(PoseThenHands(), NoPools(),
            new GenerationOptions { LineCount = 300, Seed = 5, AvoidDuplicateLines = false, LocalConflictFiltering = true },
            HandsRules);
        Assert.All(result.Lines.Where(l => l.Contains("full nelson")), l => Assert.Equal("full nelson, smile", l));
    }

    [Fact]
    public void FallsBackToWholeLineRerollWhenEverythingIsFiltered()
    {
        // 손짓 풀이 전부 충돌 후보뿐 → 풀넬슨 줄에서는 로컬로 뽑을 게 없다 → 필터 없이 뽑고 줄 재추첨이 마무리.
        var recipe = PoseThenHands();
        ((RandomPoolSlot)recipe.Slots[1]).Tags.Remove("smile");
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 9, AvoidDuplicateLines = false, LocalConflictFiltering = true },
            HandsRules);
        Assert.Equal(200, result.Lines.Count);
        Assert.Empty(result.Conflicts);   // 재추첨이 풀넬슨 줄을 전부 서서 줄로 바꿨다
        Assert.All(result.Lines, l => Assert.StartsWith("standing sex", l));
    }

    [Fact]
    public void MultiPickSlotDoesNotPickTwoMutuallyExclusiveCandidates()
    {
        var rules = new ConflictRules(new[] { new ConflictGroup("시선", new[] { "looking_up", "looking_down" }) });
        var recipe = new Recipe { Slots = { new RandomPoolSlot { Tags = { "looking_up", "looking_down", "smile" }, MinCount = 2, MaxCount = 2 } } };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 2, AvoidDuplicateLines = false, LocalConflictFiltering = true },
            rules);
        Assert.Empty(result.Conflicts);
        Assert.All(result.Lines, l => Assert.Contains("smile", l));   // 한 시선 + smile이 유일한 비충돌 조합
    }

    [Fact]
    public void OffByDefaultAndNoEffectWithoutAvoidConflicts()
    {
        Assert.False(new GenerationOptions().LocalConflictFiltering);
        var opts = new GenerationOptions { LineCount = 50, Seed = 4, AvoidDuplicateLines = false, AvoidConflicts = false };
        var gen = new WildcardGenerator();
        var a = gen.Generate(PoseThenHands(), NoPools(), opts, HandsRules).Lines;
        opts.LocalConflictFiltering = true;
        var b = gen.Generate(PoseThenHands(), NoPools(), opts, HandsRules).Lines;
        Assert.Equal(a, b);
    }

    [Fact]
    public void IntroducesIgnoresPreexistingConflictsAndRespectsExcept()
    {
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("삽입 부위", new[] { "vaginal", "anal" }, Except: new[] { "double_penetration" }),
            new ConflictGroup("시간대", new[] { "day", "night" }),
        });
        var line = new HashSet<string> { "vaginal", "day", "night" };   // day+night는 이미 깨져 있음
        Assert.True(rules.Introduces(line, new HashSet<string> { "anal" }));
        Assert.False(rules.Introduces(line, new HashSet<string> { "anal", "double_penetration" }));   // 후보가 면제 태그를 가져옴
        Assert.False(rules.Introduces(line, new HashSet<string> { "night" }));   // 원래 깨져 있던 규칙은 "새 충돌"이 아니다
        Assert.False(rules.Introduces(line, new HashSet<string> { "smile" }));
    }

    [Fact]
    public void LineTrackerMatchesIntroducesIncludingRollback()
    {
        // 생성기가 쓰는 증분 판정(LineTracker)이 기준 구현(Introduces)과 매번 같은 답을 내는지 — 상호배타·편 나눔·예산,
        // 면제, 캐릭터 단위(다인수 마커로 억제), 키를 공유하는 규칙, 줄에 이미 있는 키를 다시 가져오는 후보,
        // 풀 슬롯의 Mark/Rollback까지 무작위로 섞는다.
        var rules = new ConflictRules(new[]
        {
            new ConflictGroup("A", new[] { "a1", "a2", "a3" }),
            new ConflictGroup("B", new[] { "b1", "b2", "a1" }, ConflictScope.Character),
            new ConflictGroup("C", new[] { "a2", "c1" }, Except: new[] { "x", "y" }),
            ConflictGroup.Bipartite("D", new IReadOnlyList<string>[] { new[] { "d1", "d2" }, new[] { "d3", "a3" } }),
            ConflictGroup.WithBudget("E", "hands", 2, new[] { ("e1", 2), ("e2", 1), ("a2", 1), ("b1", 0) }),
            ConflictGroup.WithBudget("F", "zero", 0, new[] { ("f1", 1), ("c1", 0) }, except: new[] { "y" }),
        });
        var vocab = new[] { "a1", "a2", "a3", "b1", "b2", "c1", "d1", "d2", "d3", "e1", "e2", "f1", "x", "y", "2girls", "smile" };
        var tracker = rules.NewLineTracker();
        var rnd = new Random(7);

        for (int line = 0; line < 2000; line++)
        {
            tracker.BeginLine();
            var current = new HashSet<string>();
            var marks = new Stack<((int, int) Mark, HashSet<string> Snapshot)>();

            for (int step = 0; step < 12; step++)
            {
                var candidate = vocab.Where(_ => rnd.Next(6) == 0).ToHashSet();
                Assert.Equal(rules.Introduces(current, candidate), tracker.Introduces(rules.Prepare(candidate)));

                switch (rnd.Next(4))
                {
                    case 0:   // 풀 슬롯 시작: 표시해 두고 이후 키를 되돌릴 수 있게
                        marks.Push((tracker.Mark(), new HashSet<string>(current)));
                        break;
                    case 1 when marks.Count > 0:   // 풀 슬롯 끝: 되돌림
                        var (mark, snapshot) = marks.Pop();
                        tracker.Rollback(mark);
                        current = snapshot;
                        break;
                    default:   // 후보를 줄에 확정
                        tracker.AddRange(candidate);
                        current.UnionWith(candidate);
                        break;
                }
            }
        }
    }
}
