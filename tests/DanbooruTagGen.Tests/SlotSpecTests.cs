using System.Collections.ObjectModel;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary><see cref="SlotSpec.FromSlots"/>(빌더 경고·라이브러리 배지 공용)와, 역인덱스로 추린 충돌 판정이
/// 전수 순회와 같은 결과를 내는지.</summary>
public class SlotSpecTests
{
    private static ConflictRules Rules() => new(new[]
    {
        new ConflictGroup("머리 길이", new[] { "short_hair", "long_hair" }),
        new ConflictGroup("시간대", new[] { "day", "night" }),
        new ConflictGroup("인원수", new[] { "1girl", "2girls" }),
    });

    private static AlternativeGroup Group(string label, int weight, params string[] tags) =>
        new() { Label = label, Weight = weight, Tags = new ObservableCollection<string>(tags) };

    [Fact]
    public void ZeroWeightAltGroupIsIgnoredLikeTheGenerator()
    {
        // 가중치 0 그룹은 절대 안 뽑힌다 — 그 안의 night가 고정 day와 "항상" 충돌한다고 경고하면 오탐이다.
        var slots = new Slot[]
        {
            new FixedSlot { Tags = new ObservableCollection<string> { "day" } },
            new AlternativeSlot { Groups = new ObservableCollection<AlternativeGroup>
            {
                Group("기본", 1, "smile"),
                Group("꺼 둔 밤 분기", 0, "night"),
            } },
        };

        var specs = SlotSpec.FromSlots(slots, new Dictionary<string, Pool>());

        Assert.Empty(Rules().AnalyzeRecipe(specs));
        Assert.DoesNotContain(specs, s => s.Tags.Contains("night"));
    }

    [Fact]
    public void WeightedAltGroupStillCounts()
    {
        var slots = new Slot[]
        {
            new FixedSlot { Tags = new ObservableCollection<string> { "day" } },
            new AlternativeSlot { Groups = new ObservableCollection<AlternativeGroup> { Group("밤", 1, "night") } },
        };

        var hit = Assert.Single(Rules().AnalyzeRecipe(SlotSpec.FromSlots(slots, new Dictionary<string, Pool>())));
        Assert.Equal("시간대", hit.Label);
    }

    [Fact]
    public void DisabledSlotsAreSkipped()
    {
        var slots = new Slot[]
        {
            new FixedSlot { Tags = new ObservableCollection<string> { "day" } },
            new FixedSlot { Tags = new ObservableCollection<string> { "night" }, IsEnabled = false },
        };

        Assert.Empty(Rules().AnalyzeRecipe(SlotSpec.FromSlots(slots, new Dictionary<string, Pool>())));
    }

    [Fact]
    public void RandomSlotMergesInlineTagsAndChainedPoolsWithoutDuplicates()
    {
        var pool = new Pool { Id = "p1", Candidates = new ObservableCollection<string> { "long_hair", "smile" } };
        var extra = new Pool { Id = "p2", Candidates = new ObservableCollection<string> { "smile", "short_hair" } };
        var slot = new RandomPoolSlot
        {
            Tags = new ObservableCollection<string> { "smile" },
            PoolId = "p1",
            ExtraPoolIds = new ObservableCollection<string> { "p2", "missing" },
            MaxCount = 2,
        };

        var spec = Assert.Single(SlotSpec.FromSlots(new Slot[] { slot }, new Dictionary<string, Pool> { ["p1"] = pool, ["p2"] = extra }));

        Assert.Equal(new[] { "smile", "long_hair", "short_hair" }, spec.Tags);
        Assert.Equal(2, spec.MaxCount);
        Assert.False(spec.IsFixed);
    }

    [Fact]
    public void AltGroupsOfOneSlotShareAnOwnerAndDifferentSlotsDoNot()
    {
        var slots = new Slot[]
        {
            new AlternativeSlot { Groups = new ObservableCollection<AlternativeGroup> { Group("a", 1, "day"), Group("b", 1, "night") } },
            new AlternativeSlot { Groups = new ObservableCollection<AlternativeGroup> { Group("c", 1, "1girl") } },
        };

        var specs = SlotSpec.FromSlots(slots, new Dictionary<string, Pool>());

        Assert.Equal(3, specs.Count);
        Assert.Equal(specs[0].AltGroupOwnerId, specs[1].AltGroupOwnerId);
        Assert.NotEqual(specs[0].AltGroupOwnerId, specs[2].AltGroupOwnerId);
        // 같은 슬롯의 두 그룹(day / night)은 같이 못 나오므로 충돌이 아니다.
        Assert.Empty(Rules().AnalyzeRecipe(specs));
    }

    [Fact]
    public void DetectKeepsRuleFileOrderRegardlessOfTagOrder()
    {
        // 역인덱스로 규칙을 추려도 결과는 규칙 파일 순서 그대로여야 한다(태그가 나온 순서가 아니라).
        var hits = Rules().Detect(new[] { "2girls", "1girl", "night", "day", "long_hair", "short_hair" });

        Assert.Equal(new[] { "머리 길이", "시간대", "인원수" }, hits.Select(h => h.Label));
    }

    [Fact]
    public void MultiRuleResultsEqualPerRuleResultsInFileOrder()
    {
        // 무작위 줄 수천 개에서 Detect/HasConflict가 "규칙을 하나씩 따로 검사한 결과"를 파일 순서로 이은 것과 같은지 —
        // 여러 규칙이 같은 태그를 공유할 때(a1, a2) 역인덱스가 규칙을 빠뜨리거나 중복·순서 뒤바꿈 없이 모으는지 본다.
        var groups = new List<ConflictGroup>
        {
            new("A", new[] { "a1", "a2", "a3" }),
            new("B", new[] { "b1", "b2" }, ConflictScope.Character),
            new("C", new[] { "a1", "c1" }, Except: new[] { "x" }),
            ConflictGroup.Bipartite("D", new IReadOnlyList<string>[] { new[] { "d1", "d2" }, new[] { "d3" } }),
            ConflictGroup.WithBudget("E", "hands", 2, new[] { ("e1", 2), ("e2", 1), ("a2", 1) }),
        };
        var rules = new ConflictRules(groups);
        var pool = new[] { "a1", "a2", "a3", "b1", "b2", "c1", "d1", "d2", "d3", "e1", "e2", "x", "2girls", "smile", "(a1:1.2)" };
        var rnd = new Random(42);

        for (int n = 0; n < 3000; n++)
        {
            var line = pool.Where(_ => rnd.Next(4) == 0).ToList();
            var expected = groups
                .Where(g => new ConflictRules(new[] { g }).HasConflict(line))
                .Select(g => g.Label)
                .ToList();

            Assert.Equal(expected, rules.Detect(line).Select(h => h.Label));
            Assert.Equal(expected.Count > 0, rules.HasConflict(line));
        }
    }
}
