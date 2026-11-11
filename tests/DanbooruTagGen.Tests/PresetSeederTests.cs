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
    public void SyncLeavesIdenticalItemsUntouchedAndUncounted()
    {
        // 내용이 같은 항목까지 교체로 세면 퀵 갱신이 매번 "전부 바뀜"이 돼 저장·화면 전체 갱신을 매번 돌렸다.
        // 교체도 하지 않아야 화면이 들고 있는 객체(편집 중인 미리보기)가 그대로 유지된다.
        var userPool = new Pool { Id = "p1", Name = "풀", Candidates = { "a" } };
        var userRecipe = new Recipe { Id = "r1", Name = "팩", DefaultLineCount = 5 };
        var userPools = new List<Pool> { userPool };
        var userRecipes = new List<Recipe> { userRecipe };
        var changed = new List<string>();

        var (updated, _) = PresetSeeder.SyncBundled(
            userPools, new[] { new Pool { Id = "p1", Name = "풀", Candidates = { "a" } } },
            userRecipes, new[] { new Recipe { Id = "r1", Name = "팩", DefaultLineCount = 5 } },
            new HashSet<string> { "p1", "r1" }, changedRecipeIds: changed);

        Assert.Equal(0, updated);
        Assert.Same(userPool, userPools[0]);
        Assert.Same(userRecipe, userRecipes[0]);
        Assert.Empty(changed);
    }

    [Fact]
    public void SyncReportsWhichRecipesActuallyChanged()
    {
        var userRecipes = new List<Recipe>
        {
            new() { Id = "r1", Name = "같음", DefaultLineCount = 5 },
            new() { Id = "r2", Name = "바뀜", DefaultLineCount = 5 },
        };
        var changed = new List<string>();

        PresetSeeder.SyncBundled(new List<Pool>(), Array.Empty<Pool>(), userRecipes,
            new[] { new Recipe { Id = "r1", Name = "같음", DefaultLineCount = 5 }, new Recipe { Id = "r2", Name = "바뀜", DefaultLineCount = 9 } },
            new HashSet<string> { "r1", "r2" }, changedRecipeIds: changed);

        Assert.Equal(new[] { "r2" }, changed);
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
        // 장부에서도 같이 빠져야 한다(예전엔 남겨 둬서, 번들에 같은 id가 돌아와도 Seed가
        // "사용자가 지운 것"으로 보고 영영 다시 넣지 않는 유령 시딩이 됐다).
        Assert.Empty(seeded);
    }

    // ── 유령 시딩 방지: 제거 시 장부 동기화 + 복귀 시 재시딩 ─────────────────

    [Fact]
    public void SyncRemovalAlsoDropsIdsFromLedger_ButKeepsUnrelatedIds()
    {
        var userPools = new List<Pool> { BundledPool("p1"), BundledPool("p2") };
        var userRecipes = new List<Recipe> { BundledRecipe("r1"), BundledRecipe("r2") };
        // "deleted-by-user"는 사용자가 지운(목록엔 없음) 번들 항목 — 제거 대상이 아니므로 장부에 남아야 한다.
        var seeded = new HashSet<string> { "p1", "p2", "r1", "r2", "deleted-by-user" };

        var (_, removed) = PresetSeeder.SyncBundled(
            userPools, new[] { BundledPool("p2") }, userRecipes, new[] { BundledRecipe("r2") }, seeded);

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "deleted-by-user", "p2", "r2" }, seeded.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ItemRemovedFromBundleComesBackWhenBundleRestoresIt()
    {
        // 실제 시나리오: 편집 중 번들 파일이 잠깐 빠졌다(or 실수로 지웠다 되돌림) → 다음 동기화에서
        // 같은 id로 돌아오면 사용자 목록에 다시 들어와야 한다.
        var userPools = new List<Pool>();
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string>();
        var pools = new[] { BundledPool("p1") };
        var recipes = new[] { BundledRecipe("r1"), BundledRecipe("r2", "둘") };

        PresetSeeder.Seed(userPools, pools, userRecipes, recipes, seeded);

        // r2가 번들에서 빠짐
        var withoutR2 = new[] { recipes[0] };
        PresetSeeder.Seed(userPools, pools, userRecipes, withoutR2, seeded);
        var (_, removed) = PresetSeeder.SyncBundled(userPools, pools, userRecipes, withoutR2, seeded);
        Assert.Equal(1, removed);
        Assert.DoesNotContain(userRecipes, r => r.Id == "r2");

        // r2가 같은 id로 복귀
        var added = PresetSeeder.Seed(userPools, pools, userRecipes, recipes, seeded);
        PresetSeeder.SyncBundled(userPools, pools, userRecipes, recipes, seeded);

        Assert.True(added);
        Assert.Contains(userRecipes, r => r.Id == "r2");
        Assert.Contains("r2", seeded);
        Assert.Single(userRecipes, r => r.Id == "r2");
    }

    [Fact]
    public void SyncWithRemovalDisallowedKeepsItemsAndLedger()
    {
        var userRecipes = new List<Recipe> { BundledRecipe("r1") };
        var seeded = new HashSet<string> { "r1" };

        Assert.Equal(1, PresetSeeder.CountPendingRemovals(
            new List<Pool>(), Array.Empty<Pool>(), userRecipes, Array.Empty<Recipe>(), seeded));

        var (_, removed) = PresetSeeder.SyncBundled(
            new List<Pool>(), Array.Empty<Pool>(), userRecipes, Array.Empty<Recipe>(), seeded, allowRemoval: false);

        Assert.Equal(0, removed);
        Assert.Single(userRecipes);
        Assert.Contains("r1", seeded);
    }

    // ── 장부 저장 판단(PlanLedgerWrites) ──────────────────────────────────

    [Fact]
    public void LedgerPlan_NoChange_WritesNothing()
    {
        var (before, after) = PresetSeeder.PlanLedgerWrites(new[] { "a", "b" }, new HashSet<string> { "a", "b" });
        Assert.Null(before);
        Assert.Null(after);
    }

    [Fact]
    public void LedgerPlan_AddOnly_WritesAfterDataOnly()
    {
        var (before, after) = PresetSeeder.PlanLedgerWrites(new[] { "a" }, new HashSet<string> { "a", "b" });
        Assert.Null(before);
        Assert.Equal(new[] { "a", "b" }, after!.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void LedgerPlan_RemoveOnly_WritesBeforeData()
    {
        // 예전 조건(added일 때만 저장)으로는 제거만 있을 때 장부가 아예 안 저장됐다.
        var (before, after) = PresetSeeder.PlanLedgerWrites(new[] { "a", "b" }, new HashSet<string> { "a" });
        Assert.Equal(new[] { "a" }, before);
        Assert.Null(after);
    }

    [Fact]
    public void LedgerPlan_AddAndRemove_RemovesFirstThenAdds()
    {
        // 먼저 쓰는 장부엔 제거만 반영(추가분은 아직 없음), 데이터 저장 뒤 최종 장부.
        var (before, after) = PresetSeeder.PlanLedgerWrites(new[] { "a", "b" }, new HashSet<string> { "a", "c" });
        Assert.Equal(new[] { "a" }, before);
        Assert.Equal(new[] { "a", "c" }, after!.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void LedgerPlan_CrashBetweenWrites_DoesNotLeaveGhosts()
    {
        // 어느 틈에 죽어도 다음 실행 Seed가 흡수하는지: "제거 장부만 쓰고 데이터는 옛것"인 상태.
        // 데이터엔 r1(번들에서 빠진 것)과 r0이 남아 있고, 장부는 before(r1 빠짐), 번들엔 r0·r2.
        var userRecipes = new List<Recipe> { BundledRecipe("r0"), BundledRecipe("r1") };
        var (before, _) = PresetSeeder.PlanLedgerWrites(new[] { "r0", "r1" }, new HashSet<string> { "r0", "r2" });
        var seeded = new HashSet<string>(before!);

        PresetSeeder.Seed(new List<Pool>(), Array.Empty<Pool>(),
            userRecipes, new[] { BundledRecipe("r0"), BundledRecipe("r2", "둘") }, seeded);

        Assert.Single(userRecipes, r => r.Id == "r0");   // 중복 없음
        Assert.Single(userRecipes, r => r.Id == "r2");   // 추가분 정상 진입
        Assert.Contains("r2", seeded);
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

    [Fact]
    public void TwoBundledRecipesWithSameNameBothGetInstalled()
    {
        // 이름이 같은 번들 팩이 둘이면, 예전엔 뒤엣것이 "이미 있는 이름"으로 걸러져
        // 추가되지 않는데도 시딩됨으로 기록돼 영영 안 들어왔다(장부에만 남는 유령).
        var userRecipes = new List<Recipe>();
        var seeded = new HashSet<string>();

        PresetSeeder.Seed(
            new List<Pool>(), Array.Empty<Pool>(),
            userRecipes, new[] { BundledRecipe("r1", "같은 이름"), BundledRecipe("r2", "같은 이름") }, seeded);

        Assert.Equal(new[] { "r1", "r2" }, userRecipes.Select(r => r.Id));
    }

    [Fact]
    public void LegacyCopyWithSameNameIsAdoptedInsteadOfDuplicated()
    {
        // 반대로 id 도입 전 설치된 사본(번들 id가 아닌 것)이 같은 이름으로 있으면
        // 이미 설치된 것으로 보고 중복 추가하지 않는다(기존 동작 유지).
        var legacy = new Recipe { Id = "legacy-guid", Name = "온천 여행" };
        var userRecipes = new List<Recipe> { legacy };
        var seeded = new HashSet<string>();

        PresetSeeder.Seed(
            new List<Pool>(), Array.Empty<Pool>(),
            userRecipes, new[] { BundledRecipe("preset-r-s002", "온천 여행") }, seeded);

        Assert.Single(userRecipes);
        Assert.Contains("preset-r-s002", seeded);
    }
}
