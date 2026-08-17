using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class RecipeStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recipe_{Guid.NewGuid():N}.json");
        try
        {
            var recipe = new Recipe { Name = "r", Category = "촉수", Slots = { new RandomPoolSlot { PoolId = "p", MaxCount = 2 } } };
            RecipeStore.Save(recipe, path);
            var back = RecipeStore.Load(path);
            Assert.Equal("r", back.Name);
            Assert.Equal("촉수", back.Category);
            Assert.IsType<RandomPoolSlot>(back.Slots[0]);
        }
        finally { File.Delete(path); }
    }
}
