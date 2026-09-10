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
            Assert.NotEqual("", error);
        }
        finally
        {
            hold.Dispose();
            Directory.Delete(dir, recursive: true);
        }
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
