using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>생성 직전에 사용자 정의 슬롯(훅)을 레시피에 끼워넣는 로직. 레시피마다 슬롯 수가
/// 달라서 위치 보정이 필요하고, 훅 설정이 잘못돼 있어도 생성 전체가 죽으면 안 된다.</summary>
public class HookApplierTests
{
    private static Recipe RecipeWith(params string[] labels) => new()
    {
        Name = "테스트",
        Slots = labels.Select(l => (Slot)new FixedSlot { Label = l, Tags = { "tag_" + l } }).ToList(),
    };

    private static GenerationHook Hook(string name, HookPlacement placement, int index = 0) => new()
    {
        Name = name,
        IsEnabled = true,
        Placement = placement,
        Index = index,
        Kind = HookSlotKind.Fixed,
        Tags = { "hook_" + name },
    };

    private static readonly Dictionary<string, Pool> NoPools = new();

    [Fact]
    public void FrontHookGoesToTheVeryFront()
    {
        var recipe = RecipeWith("a", "b", "c");
        HookApplier.Apply(recipe, new[] { Hook("H", HookPlacement.Front) }, NoPools);
        Assert.Equal(new[] { "H", "a", "b", "c" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void BackHookGoesToTheVeryEnd()
    {
        var recipe = RecipeWith("a", "b", "c");
        HookApplier.Apply(recipe, new[] { Hook("H", HookPlacement.Back) }, NoPools);
        Assert.Equal(new[] { "a", "b", "c", "H" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void AtIndexInsertsBeforeThatSlot()
    {
        var recipe = RecipeWith("a", "b", "c");
        HookApplier.Apply(recipe, new[] { Hook("H", HookPlacement.AtIndex, 2) }, NoPools);
        Assert.Equal(new[] { "a", "b", "H", "c" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void IndexBeyondSlotCountClampsToTheEnd()
    {
        var recipe = RecipeWith("a", "b");
        HookApplier.Apply(recipe, new[] { Hook("H", HookPlacement.AtIndex, 99) }, NoPools);
        Assert.Equal(new[] { "a", "b", "H" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void NegativeIndexClampsToTheFront()
    {
        var recipe = RecipeWith("a", "b");
        HookApplier.Apply(recipe, new[] { Hook("H", HookPlacement.AtIndex, -5) }, NoPools);
        Assert.Equal(new[] { "H", "a", "b" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void DisabledHookIsNotApplied()
    {
        var recipe = RecipeWith("a");
        var hook = Hook("H", HookPlacement.Front);
        hook.IsEnabled = false;
        HookApplier.Apply(recipe, new[] { hook }, NoPools);
        Assert.Equal(new[] { "a" }, recipe.Slots.Select(s => s.Label));
    }

    /// <summary>훅을 하나씩 Insert하면 먼저 넣은 훅이 뒤 훅의 목표 인덱스를 밀어 버린다.
    /// 모든 목표 인덱스를 원본 슬롯 수 기준으로 먼저 계산해야 결과가 예측 가능하다.</summary>
    [Fact]
    public void MultipleHooksUseOriginalIndicesNotShiftedOnes()
    {
        var recipe = RecipeWith("a", "b", "c", "d");
        var hooks = new[]
        {
            Hook("front", HookPlacement.Front),
            Hook("mid", HookPlacement.AtIndex, 2),
            Hook("back", HookPlacement.Back),
        };
        HookApplier.Apply(recipe, hooks, NoPools);
        Assert.Equal(new[] { "front", "a", "b", "mid", "c", "d", "back" },
                     recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void HooksAtTheSameIndexKeepOrderAscending()
    {
        var recipe = RecipeWith("a");
        var first = Hook("first", HookPlacement.Front);
        first.Order = 1;
        var second = Hook("second", HookPlacement.Front);
        second.Order = 2;
        HookApplier.Apply(recipe, new[] { second, first }, NoPools);
        Assert.Equal(new[] { "first", "second", "a" }, recipe.Slots.Select(s => s.Label));
    }

    [Fact]
    public void HookWithNoCandidatesIsSkippedAndReported()
    {
        var recipe = RecipeWith("a");
        var empty = new GenerationHook { Name = "빈훅", IsEnabled = true, Kind = HookSlotKind.Fixed };
        var skipped = HookApplier.Apply(recipe, new[] { empty }, NoPools);
        Assert.Equal(new[] { "a" }, recipe.Slots.Select(s => s.Label));
        Assert.Equal(new[] { "빈훅" }, skipped);
    }

    [Fact]
    public void RandomHookBecomesRandomPoolSlotWithPoolReference()
    {
        var recipe = RecipeWith("a");
        var pools = new Dictionary<string, Pool>
        {
            ["p1"] = new Pool { Id = "p1", Name = "조명", Candidates = { "backlighting", "rim_light" } },
        };
        var hook = new GenerationHook
        {
            Name = "조명훅",
            IsEnabled = true,
            Placement = HookPlacement.Back,
            Kind = HookSlotKind.Random,
            PoolId = "p1",
            MinCount = 1,
            MaxCount = 1,
        };
        HookApplier.Apply(recipe, new[] { hook }, pools);
        var slot = Assert.IsType<RandomPoolSlot>(recipe.Slots[^1]);
        Assert.Equal("p1", slot.PoolId);
        Assert.Equal(1, slot.MinCount);
        Assert.Equal(1, slot.MaxCount);
    }

    /// <summary>고정 훅에는 PoolId 개념이 없다(FixedSlot에 그 필드가 없음). 풀을 참조하면 그
    /// 후보를 전부 펼쳐 넣는 것이 "이 풀의 태그를 항상 붙인다"는 고정 훅의 자연스러운 의미다.</summary>
    [Fact]
    public void FixedHookExpandsReferencedPoolIntoItsTags()
    {
        var recipe = RecipeWith("a");
        var pools = new Dictionary<string, Pool>
        {
            ["p1"] = new Pool { Id = "p1", Name = "품질", Candidates = { "masterpiece", "highres" } },
        };
        var hook = new GenerationHook
        {
            Name = "품질훅",
            IsEnabled = true,
            Placement = HookPlacement.Front,
            Kind = HookSlotKind.Fixed,
            PoolId = "p1",
            Tags = { "absurdres" },
        };
        HookApplier.Apply(recipe, new[] { hook }, pools);
        var slot = Assert.IsType<FixedSlot>(recipe.Slots[0]);
        Assert.Equal(new[] { "absurdres", "masterpiece", "highres" }, slot.Tags);
    }

    /// <summary>MinCount가 후보 수보다 크면 WildcardGenerator.Validate가 예외를 던져 생성
    /// 전체가 죽는다. 훅 설정 하나가 잘못됐다고 생성을 못 하게 만들 이유가 없다.</summary>
    [Fact]
    public void MinCountIsClampedToTheCandidateCount()
    {
        var recipe = RecipeWith("a");
        var hook = new GenerationHook
        {
            Name = "과한훅",
            IsEnabled = true,
            Placement = HookPlacement.Back,
            Kind = HookSlotKind.Random,
            Tags = { "x", "y" },
            MinCount = 5,
            MaxCount = 9,
        };
        HookApplier.Apply(recipe, new[] { hook }, NoPools);
        var slot = Assert.IsType<RandomPoolSlot>(recipe.Slots[^1]);
        Assert.Equal(2, slot.MinCount);
        Assert.Equal(2, slot.MaxCount);
    }

    [Fact]
    public void MaxCountBelowMinCountIsRaisedToMinCount()
    {
        var recipe = RecipeWith("a");
        var hook = new GenerationHook
        {
            Name = "뒤집힌훅",
            IsEnabled = true,
            Placement = HookPlacement.Back,
            Kind = HookSlotKind.Random,
            Tags = { "x", "y", "z" },
            MinCount = 2,
            MaxCount = 1,
        };
        HookApplier.Apply(recipe, new[] { hook }, NoPools);
        var slot = Assert.IsType<RandomPoolSlot>(recipe.Slots[^1]);
        Assert.Equal(2, slot.MinCount);
        Assert.Equal(2, slot.MaxCount);
    }

    [Fact]
    public void BlankTagsAreIgnored()
    {
        var recipe = RecipeWith("a");
        var hook = new GenerationHook
        {
            Name = "공백훅",
            IsEnabled = true,
            Placement = HookPlacement.Back,
            Kind = HookSlotKind.Fixed,
            Tags = { "  ", "", " real_tag " },
        };
        HookApplier.Apply(recipe, new[] { hook }, NoPools);
        var slot = Assert.IsType<FixedSlot>(recipe.Slots[^1]);
        Assert.Equal(new[] { "real_tag" }, slot.Tags);
    }

    /// <summary>훅이 만들어낸 슬롯은 평범한 슬롯이라 기존 검증을 그대로 통과해야 한다 —
    /// 이게 WildcardGenerator를 수정하지 않아도 되는 이유다.</summary>
    [Fact]
    public void AppliedRecipeStillPassesGeneratorValidation()
    {
        var recipe = RecipeWith("a");
        var pools = new Dictionary<string, Pool>
        {
            ["p1"] = new Pool { Id = "p1", Name = "조명", Candidates = { "backlighting", "rim_light" } },
        };
        var hook = new GenerationHook
        {
            Name = "조명훅",
            IsEnabled = true,
            Placement = HookPlacement.Back,
            Kind = HookSlotKind.Random,
            PoolId = "p1",
            MinCount = 1,
            MaxCount = 2,
        };
        HookApplier.Apply(recipe, new[] { hook }, pools);
        WildcardGenerator.Validate(recipe, pools);
    }
}
