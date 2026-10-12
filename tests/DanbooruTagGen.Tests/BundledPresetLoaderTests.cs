using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>번들 프리셋(data/presets) 읽기의 실패를 예외가 아니라 값으로 돌려주는 계약.
/// 예전엔 MainViewModel이 RecipeLibraryStore.Load를 맨몸으로 불러서, 레시피 파일 하나에
/// id가 겹치거나(작성 중) 파일이 잠겨 있으면 앱 시작과 프리셋 자동 갱신이 통째로 죽었다.</summary>
public class BundledPresetLoaderTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), $"bundle_{Guid.NewGuid():N}");

    [Fact]
    public void LoadsPoolsAndRecipes()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "recipes"));
            var poolsFile = Path.Combine(dir, "pools.json");
            PoolStore.Save(new[] { new Pool { Id = "p1", Name = "[축] 조명" } }, poolsFile);
            JsonStore.SaveAtomic(new Recipe { Id = "r1", Name = "팩" }, Path.Combine(dir, "recipes", "r1.json"));

            var ok = BundledPresetLoader.TryLoad(poolsFile, Path.Combine(dir, "recipes"), out var presets, out var error);

            Assert.True(ok);
            Assert.Equal("", error);
            Assert.Single(presets.Pools);
            Assert.Single(presets.Recipes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReportsErrorInsteadOfThrowingWhenRecipeIdsCollide()
    {
        var dir = NewDir();
        try
        {
            var recipesDir = Path.Combine(dir, "recipes");
            Directory.CreateDirectory(recipesDir);
            JsonStore.SaveAtomic(new Recipe { Id = "same", Name = "A" }, Path.Combine(recipesDir, "a.json"));
            JsonStore.SaveAtomic(new Recipe { Id = "same", Name = "B" }, Path.Combine(recipesDir, "b.json"));

            var ok = BundledPresetLoader.TryLoad(Path.Combine(dir, "pools.json"), recipesDir, out _, out var error);

            Assert.False(ok);
            Assert.Contains("same", error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReportsErrorInsteadOfThrowingWhenRecipeFileStaysLocked()
    {
        var dir = NewDir();
        var recipesDir = Path.Combine(dir, "recipes");
        Directory.CreateDirectory(recipesDir);
        var file = Path.Combine(recipesDir, "locked.json");
        JsonStore.SaveAtomic(new Recipe { Id = "r1", Name = "팩" }, file);
        using var hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var ok = BundledPresetLoader.TryLoad(Path.Combine(dir, "pools.json"), recipesDir, out _, out var error);

            Assert.False(ok);
            Assert.Contains("locked.json", error);
        }
        finally
        {
            hold.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── 유령 시딩 방지: 번들 쪽 손상은 "없는 것"이 아니라 실패로 ─────────────
    // 예전엔 깨진 레시피 파일을 조용히 건너뛰고 TryLoad가 true를 돌려줘서, SyncBundled가
    // 그 레시피를 "번들에서 삭제됨"으로 오인해 사용자 데이터에서 지웠다.

    /// <summary>정상 pools.json + 정상 레시피 1개 + (내용 지정) 레시피 1개로 번들 폴더를 만든다.</summary>
    private static (string Dir, string PoolsFile, string RecipesDir) MakeBundle(string badFileName, string badContent)
    {
        var dir = NewDir();
        var recipesDir = Path.Combine(dir, "recipes");
        Directory.CreateDirectory(recipesDir);
        var poolsFile = Path.Combine(dir, "pools.json");
        PoolStore.Save(new[] { new Pool { Id = "p1", Name = "[축] 조명" } }, poolsFile);
        JsonStore.SaveAtomic(new Recipe { Id = "good", Name = "멀쩡" }, Path.Combine(recipesDir, "good.json"));
        File.WriteAllText(Path.Combine(recipesDir, badFileName), badContent);
        return (dir, poolsFile, recipesDir);
    }

    [Fact]
    public void BrokenBundledRecipeJson_FailsWithFileName()
    {
        var (dir, poolsFile, recipesDir) = MakeBundle("preset-r-broken.json", "{ \"id\": \"broken\", \"name\": \"깨짐\", ");
        try
        {
            var ok = BundledPresetLoader.TryLoad(poolsFile, recipesDir, out var presets, out var error);

            Assert.False(ok);
            Assert.Contains("preset-r-broken.json", error);
            Assert.Empty(presets.Recipes);   // 부분 결과를 흘리지 않는다
            Assert.False(File.Exists(Path.Combine(recipesDir, "preset-r-broken.json.bak")),
                "번들 폴더에 .bak을 만들면 안 된다");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BrokenBundledPoolsJson_Fails()
    {
        var (dir, poolsFile, recipesDir) = MakeBundle("ok2.json", "{ \"id\": \"ok2\", \"name\": \"둘\" }");
        try
        {
            File.WriteAllText(poolsFile, "[ { \"id\": \"p1\", \"name\": ");

            var ok = BundledPresetLoader.TryLoad(poolsFile, recipesDir, out _, out var error);

            Assert.False(ok);
            Assert.Contains("pools.json", error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BundledRecipeWithDecimalWeight_Fails()
    {
        // 실제 사고: AlternativeGroup.Weight(int)에 0.3 → 역직렬화 실패 → 레시피가 조용히 빠짐.
        var json = """
        {
          "id": "decimal-weight",
          "name": "소수 가중치",
          "slots": [
            { "type": "alternative", "groups": [ { "label": "A", "tags": ["smile"], "weight": 0.3 } ] }
          ]
        }
        """;
        var (dir, poolsFile, recipesDir) = MakeBundle("decimal.json", json);
        try
        {
            var ok = BundledPresetLoader.TryLoad(poolsFile, recipesDir, out _, out var error);

            Assert.False(ok);
            Assert.Contains("decimal.json", error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UserDataLoadStaysLenient_FallsBackAndBacksUp()
    {
        // 회귀 방지: 사용자 데이터(AppData의 recipes.json/pools.json)는 계속 관대하게 —
        // 깨져도 앱 시작을 막지 않고 빈 목록 + .bak 백업.
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        var recipesFile = Path.Combine(dir, "recipes.json");
        var poolsFile = Path.Combine(dir, "pools.json");
        try
        {
            File.WriteAllText(recipesFile, "[ { \"id\": ");
            File.WriteAllText(poolsFile, "not json");

            Assert.Empty(RecipeLibraryStore.Load(recipesFile));
            Assert.Empty(PoolStore.Load(poolsFile));
            Assert.True(File.Exists(recipesFile + ".bak"));
            Assert.True(File.Exists(poolsFile + ".bak"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── 런타임 자동 복구: 깨진 저장 → 재시도 실패 → 유효한 저장 ───────────────

    [Fact]
    public void RetryGate_ResetsOnEveryChangeEvent()
    {
        var gate = new PresetSyncRetryGate(maxRetries: 3);

        // 깨진 저장: 변경 이벤트 1번, 재시도 3번 모두 실패 → 예산 소진.
        gate.OnSourceChanged();
        Assert.True(gate.TryScheduleRetry());
        Assert.True(gate.TryScheduleRetry());
        Assert.True(gate.TryScheduleRetry());
        Assert.False(gate.TryScheduleRetry());

        // 다음 저장(변경 이벤트): 예산이 새로 생겨 첫 읽기가 잠금에 걸려도 다시 재시도할 수 있다.
        gate.OnSourceChanged();
        Assert.Equal(0, gate.Retries);
        Assert.True(gate.TryScheduleRetry());
    }

    [Fact]
    public void BrokenSaveThenValidSave_SyncsWithoutRestartAndLosesNothing()
    {
        // 사용자 워크플로 재현: 레시피 JSON을 여러 번 나눠 편집하다 중간 저장이 깨짐 → 워처가
        // 읽어 재시도까지 전부 실패 → 다음 저장에서 JSON이 다시 유효해짐 → 앱 재시작 없이 반영.
        var (dir, poolsFile, recipesDir) = MakeBundle("edited.json", """{ "id": "edited", "name": "v1" }""");
        try
        {
            var gate = new PresetSyncRetryGate(maxRetries: 3);
            var userPools = new List<Pool>();
            var userRecipes = new List<Recipe>();
            var seeded = new HashSet<string>(StringComparer.Ordinal);

            // QuickSyncBundledPresets와 같은 흐름(MainViewModel의 워처 경로를 WPF 없이 재현).
            bool Sync()
            {
                BundledPresets b;
                while (!BundledPresetLoader.TryLoad(poolsFile, recipesDir, out b, out _))
                    if (!gate.TryScheduleRetry()) return false;
                gate.OnSucceeded();
                PresetSeeder.Seed(userPools, b.Pools, userRecipes, b.Recipes, seeded);
                PresetSeeder.SyncBundled(userPools, b.Pools, userRecipes, b.Recipes, seeded);
                return true;
            }

            gate.OnSourceChanged();
            Assert.True(Sync());
            Assert.Contains(userRecipes, r => r.Id == "edited");

            // 1) 깨진 중간 저장 — 동기화 실패, 하지만 사용자 데이터·장부는 그대로여야 한다.
            File.WriteAllText(Path.Combine(recipesDir, "edited.json"), """{ "id": "edited", "name": "v2", """);
            gate.OnSourceChanged();
            Assert.False(Sync());
            Assert.Equal(gate.MaxRetries, gate.Retries);       // 예산 소진
            Assert.Contains(userRecipes, r => r.Id == "edited");  // 예전엔 여기서 지워졌다
            Assert.Contains("edited", seeded);

            // 2) 유효한 저장 — 변경 이벤트로 예산 리셋 → 정상 동기화, 내용 갱신.
            File.WriteAllText(Path.Combine(recipesDir, "edited.json"), """{ "id": "edited", "name": "v2" }""");
            gate.OnSourceChanged();
            Assert.True(Sync());
            Assert.Equal("v2", Assert.Single(userRecipes, r => r.Id == "edited").Name);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReportsEmptyWhenNothingBundled()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            var ok = BundledPresetLoader.TryLoad(Path.Combine(dir, "pools.json"), Path.Combine(dir, "recipes"), out _, out var error);

            Assert.False(ok);
            Assert.NotEqual("", error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
