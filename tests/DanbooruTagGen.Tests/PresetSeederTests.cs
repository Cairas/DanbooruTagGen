using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class PresetSeederTests
{
    private static Pool BundledPool(string id) => new() { Id = id, Name = "[축] " + id, Candidates = { "smile" } };
    private static Recipe BundledRecipe(string id, string name = "팩") => new() { Id = id, Name = name };

    [Fact]
    public void AddsNewBundledItemsAndMarksSeeded()
    {
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string>();

        var changed = PresetSeeder.Seed(
            userPools, new[] { BundledPool("p1") },
            userRecipes, new[] { BundledRecipe("r1") }, seeded);

        Assert.True(changed);
        Assert.Single(userPools);
        Assert.Single(userRecipes);
        Assert.Contains("p1", seeded);
        Assert.Contains("r1", seeded);
    }

    [Fact]
    public void DeletedPresetDoesNotComeBack()
    {
        // 시딩된 프리셋을 사용자가 지운 상태(seeded에 id는 있고 목록엔 없음) → 다시 추가 안 됨.
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string> { "p1", "r1" };

        var changed = PresetSeeder.Seed(
            userPools, new[] { BundledPool("p1") },
            userRecipes, new[] { BundledRecipe("r1") }, seeded);

        Assert.False(changed);
        Assert.Empty(userPools);
        Assert.Empty(userRecipes);
    }

    [Fact]
    public void ExistingSameIdPoolIsNotDuplicated()
    {
        // 시딩 도입 전에 같은 id의 풀이 이미 사용자 파일에 설치돼 있던 과거 상태 → 중복 추가 없이 시딩 기록만.
        var existing = BundledPool("p1");
        var userPools = new List<Pool> { existing };
        var seeded = new HashSet<string>();

        var changed = PresetSeeder.Seed(
            userPools, new[] { BundledPool("p1") },
            new List<Recipe>(), Array.Empty<Recipe>(), seeded);

        Assert.True(changed); // seeded 기록이 바뀌었으므로 저장은 필요
        Assert.Single(userPools);
        Assert.Same(existing, userPools[0]);
        Assert.Contains("p1", seeded);
    }

    [Fact]
    public void ExistingSameNameRecipeIsNotDuplicated()
    {
        // 시딩 도입 전 설치본은 id가 없어서(랜덤 GUID) 이름으로 중복을 막는다.
        var userRecipes = new List<Recipe> { new() { Name = "온천 여행" } };
        var seeded = new HashSet<string>();

        PresetSeeder.Seed(
            new List<Pool>(), Array.Empty<Pool>(),
            userRecipes, new[] { BundledRecipe("r1", "온천 여행") }, seeded);

        Assert.Single(userRecipes);
        Assert.Contains("r1", seeded);
    }

    [Fact]
    public void NewBundledItemArrivesForExistingUser()
    {
        // 업데이트로 번들에 새 프리셋이 추가된 시나리오: 기존 것은 시딩됨, 새것만 들어온다.
        var userPools = new List<Pool> { BundledPool("p1") };
        var seeded = new HashSet<string> { "p1" };

        var changed = PresetSeeder.Seed(
            userPools, new[] { BundledPool("p1"), BundledPool("p2") },
            new List<Recipe>(), Array.Empty<Recipe>(), seeded);

        Assert.True(changed);
        Assert.Equal(2, userPools.Count);
        Assert.Contains("p2", seeded);
    }

    [Fact]
    public void SecondRunIsNoOp()
    {
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string>();
        var bundledPools = new[] { BundledPool("p1") };
        var bundledRecipes = new[] { BundledRecipe("r1") };

        PresetSeeder.Seed(userPools, bundledPools, userRecipes, bundledRecipes, seeded);
        var secondRun = PresetSeeder.Seed(userPools, bundledPools, userRecipes, bundledRecipes, seeded);

        Assert.False(secondRun);
        Assert.Single(userPools);
        Assert.Single(userRecipes);
    }

    [Fact]
    public void RecipeIdRoundTripsThroughJson()
    {
        // Recipe.Id가 저장/로드에서 유지돼야 시딩 판단이 안정적이다.
        var path = Path.Combine(Path.GetTempPath(), $"recipes_id_{Guid.NewGuid():N}.json");
        try
        {
            RecipeLibraryStore.Save(new[] { new Recipe { Id = "preset-r-s001", Name = "x" } }, path);
            var back = RecipeLibraryStore.Load(path);
            Assert.Equal("preset-r-s001", back[0].Id);
        }
        finally { File.Delete(path); }
    }

    // ── SyncBundled: 이미 시딩된 번들 항목의 '내용 갱신' 경로 ──────────────

    [Fact]
    public void SyncUpdatesSeededItemsInPlace()
    {
        // Seed는 한 번만 주입하므로 번들이 보강돼도 사용자 데이터는 옛 내용 그대로다.
        // SyncBundled가 그 내용을 최신본으로 교체해야 한다.
        var userPools = new List<Pool> { new() { Id = "p1", Name = "옛이름", Candidates = { "old" } } };
        var userRecipes = new List<Recipe> { new() { Id = "r1", Name = "팩", DefaultLineCount = 1 } };

        var newPool = new Pool { Id = "p1", Name = "[축] 새이름", Candidates = { "a", "b" } };
        var newRecipe = new Recipe { Id = "r1", Name = "팩", DefaultLineCount = 100 };
        var seeded = new HashSet<string> { "p1", "r1" };

        var (updated, removed) = PresetSeeder.SyncBundled(userPools, new[] { newPool }, userRecipes, new[] { newRecipe }, seeded);

        Assert.Equal(2, updated);
        Assert.Equal(0, removed);
        Assert.Equal("[축] 새이름", userPools[0].Name);
        Assert.Equal(new[] { "a", "b" }, userPools[0].Candidates);
        Assert.Equal(100, userRecipes[0].DefaultLineCount);
    }

    [Fact]
    public void SyncDoesNotResurrectDeletedPresets()
    {
        // 사용자가 지운 번들 항목은 Sync가 되살리면 안 된다(Seed의 seed-once 취지 유지).
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string> { "p1", "r1" };

        var (updated, removed) = PresetSeeder.SyncBundled(
            userPools, new[] { BundledPool("p1") }, userRecipes, new[] { BundledRecipe("r1") }, seeded);

        Assert.Equal(0, updated);
        Assert.Equal(0, removed);
        Assert.Empty(userPools);
        Assert.Empty(userRecipes);
    }

    [Fact]
    public void SyncRestoresMissingPoolThatARecipeStillReferences()
    {
        // 실제 장애: 사용자 데이터에서 번들 풀 하나가 사라졌는데, Seed는 이미 시딩된 id라
        // 다시 안 넣고 Sync는 "지운 건 안 되살린다"고 건너뛰어, 그 풀을 참조하는 레시피가
        // "참조하는 풀을 찾을 수 없습니다"로 생성 불가가 됐다. 참조가 살아 있으면 복구해야 한다.
        // 참조는 동기화가 끝난 뒤의 레시피 기준으로 판정한다 — 번들 레시피가 그 풀을 쓰는 한
        // 사용자 사본이 무엇이든 생성에는 번들본이 쓰이기 때문이다.
        static Recipe WithPoolSlot(string id) => new()
        {
            Id = id,
            Name = "팩",
            Slots = { new RandomPoolSlot { Label = "배경", PoolId = "p1", MinCount = 1, MaxCount = 1 } },
        };

        var userPools = new List<Pool>();                 // p1이 유실된 상태
        var userRecipes = new List<Recipe> { WithPoolSlot("r1") };
        var seeded = new HashSet<string> { "p1", "r1" };

        var (updated, removed) = PresetSeeder.SyncBundled(
            userPools, new[] { BundledPool("p1") }, userRecipes, new[] { WithPoolSlot("r1") }, seeded);

        Assert.Equal(0, removed);
        Assert.Single(userPools);
        Assert.Equal("p1", userPools[0].Id);
        Assert.True(updated >= 1);
    }

    [Fact]
    public void SyncDoesNotRestoreDeletedPoolNobodyReferences()
    {
        // 참조가 없으면 사용자의 삭제 의사를 그대로 존중한다(위 복구가 과잉 적용되지 않게).
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe> { new() { Id = "r1", Name = "팩" } };
        var seeded = new HashSet<string> { "p1", "r1" };

        PresetSeeder.SyncBundled(
            userPools, new[] { BundledPool("p1") }, userRecipes, new[] { BundledRecipe("r1") }, seeded);

        Assert.Empty(userPools);
    }

    [Fact]
    public void SyncLeavesUserCreatedItemsAlone()
    {
        var mine = new Pool { Id = "mine", Name = "내풀", Candidates = { "x" } };
        var myRecipe = new Recipe { Id = "mine-r", Name = "내레시피" };
        var userPools = new List<Pool> { mine };
        var userRecipes = new List<Recipe> { myRecipe };
        var seeded = new HashSet<string>(); // "mine"/"mine-r"은 시딩된 적 없는 자작 항목

        PresetSeeder.SyncBundled(userPools, new[] { BundledPool("p1") }, userRecipes, new[] { BundledRecipe("r1") }, seeded);

        Assert.Same(mine, userPools[0]);
        Assert.Same(myRecipe, userRecipes[0]);
    }

    [Fact]
    public void SyncRemovesUserItemsWhoseBundledPresetWasDeleted()
    {
        // p1/r1은 한때 번들에 있어 시딩됐지만(seededIds에 기록됨), 이번 번들에서는 완전히
        // 빠졌다(제작자가 중복 팩을 지운 경우) → 사용자 라이브러리에서도 같이 제거돼야 한다.
        var userPools = new List<Pool> { new() { Id = "p1", Name = "지워질 풀" } };
        var userRecipes = new List<Recipe> { new() { Id = "r1", Name = "지워질 팩" } };
        var seeded = new HashSet<string> { "p1", "r1" };

        var (updated, removed) = PresetSeeder.SyncBundled(
            userPools, Array.Empty<Pool>(), userRecipes, Array.Empty<Recipe>(), seeded);

        Assert.Equal(0, updated);
        Assert.Equal(2, removed);
        Assert.Empty(userPools);
        Assert.Empty(userRecipes);
    }

    [Fact]
    public void SyncDoesNotRemoveUnseededUserItems()
    {
        // seededIds에 없는 항목(자작품이거나 id 도입 전 레거시 사본)은 번들에 없어도 지우면 안 된다.
        var userPools = new List<Pool> { new() { Id = "mine", Name = "내풀" } };
        var userRecipes = new List<Recipe> { new() { Id = "mine-r", Name = "내레시피" } };
        var seeded = new HashSet<string>();

        var (_, removed) = PresetSeeder.SyncBundled(
            userPools, Array.Empty<Pool>(), userRecipes, Array.Empty<Recipe>(), seeded);

        Assert.Equal(0, removed);
        Assert.Single(userPools);
        Assert.Single(userRecipes);
    }

    [Fact]
    public void SyncAdoptsLegacyRecipeCopyByName()
    {
        // id 도입 전 설치된 사본은 번들과 id가 다르다. 이름으로 찾아 최신본으로 교체해야
        // 영영 갱신되지 않는 상태를 막는다.
        var userRecipes = new List<Recipe> { new() { Id = "legacy-guid", Name = "온천 여행", DefaultLineCount = 1 } };
        var bundled = new Recipe { Id = "preset-r-s002", Name = "온천 여행", DefaultLineCount = 100 };

        var (updated, _) = PresetSeeder.SyncBundled(new List<Pool>(), Array.Empty<Pool>(), userRecipes, new[] { bundled }, new HashSet<string>());

        Assert.Equal(1, updated);
        Assert.Single(userRecipes);
        Assert.Equal("preset-r-s002", userRecipes[0].Id);
        Assert.Equal(100, userRecipes[0].DefaultLineCount);
    }

    [Fact]
    public void SyncMatchesPoolsByIdOnly_SoRecipePoolRefsStayIntact()
    {
        // 이름으로 풀을 교체하면 Id가 바뀌어 그 풀을 참조하는 레시피의 poolId가 끊긴다.
        // 같은 이름이어도 Id가 다르면 건드리지 않아야 한다.
        var legacyPool = new Pool { Id = "legacy-pool", Name = "[축] 조명·빛", Candidates = { "old" } };
        var userPools = new List<Pool> { legacyPool };
        var bundled = new Pool { Id = "preset-p-0001", Name = "[축] 조명·빛", Candidates = { "new" } };

        var (updated, _) = PresetSeeder.SyncBundled(userPools, new[] { bundled }, new List<Recipe>(), Array.Empty<Recipe>(), new HashSet<string>());

        Assert.Equal(0, updated);
        Assert.Same(legacyPool, userPools[0]);
        Assert.Equal("legacy-pool", userPools[0].Id);
    }
}
