using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class RecipeLibraryStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recipes_{Guid.NewGuid():N}.json");
        try
        {
            var recipes = new[] { new Recipe { Name = "야외", Slots = { new FixedSlot { Tags = { "1girl" } } } } };
            RecipeLibraryStore.Save(recipes, path);
            var back = RecipeLibraryStore.Load(path);
            Assert.Single(back);
            Assert.Equal("야외", back[0].Name);
            Assert.IsType<FixedSlot>(back[0].Slots[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMissingFileReturnsEmpty()
    {
        Assert.Empty(RecipeLibraryStore.Load(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void LoadDirectoryReadsOneRecipePerJsonFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"recipes_dir_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            RecipeLibraryStore.Save(new[] { new Recipe { Id = "r2", Name = "둘" } }, Path.Combine(dir, "legacy-array.json"));
            JsonStore.SaveAtomic(new Recipe { Id = "r1", Name = "하나" }, Path.Combine(dir, "one.json"));
            File.Delete(Path.Combine(dir, "legacy-array.json"));
            JsonStore.SaveAtomic(new Recipe { Id = "r2", Name = "둘" }, Path.Combine(dir, "two.json"));

            var result = RecipeLibraryStore.Load(dir);

            Assert.Equal(new[] { "r1", "r2" }, result.Select(r => r.Id).OrderBy(id => id));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDirectoryThrowsOnDuplicateRecipeId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"recipes_dup_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            JsonStore.SaveAtomic(new Recipe { Id = "same", Name = "A" }, Path.Combine(dir, "a.json"));
            JsonStore.SaveAtomic(new Recipe { Id = "same", Name = "B" }, Path.Combine(dir, "b.json"));

            Assert.Throws<InvalidDataException>(() => RecipeLibraryStore.Load(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadCorruptedFileReturnsEmptyInsteadOfThrowing()
    {
        // 앱 생성자 단계에서 언가드로 호출되던 Load가 파일 하나 깨졌다고 앱을 통째로
        // 못 띄우게 만들던 문제의 회귀 테스트.
        var path = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json ][");
            var result = RecipeLibraryStore.Load(path);
            Assert.Empty(result);
            Assert.True(File.Exists(path + ".bak"), "손상 파일은 .bak로 백업돼야 한다");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
