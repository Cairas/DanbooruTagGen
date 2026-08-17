using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class PoolStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pools_{Guid.NewGuid():N}.json");
        try
        {
            var pools = new[] { new Pool { Id = "p1", Name = "표정", Candidates = { "smile" } } };
            PoolStore.Save(pools, path);
            var back = PoolStore.Load(path);
            Assert.Single(back);
            Assert.Equal("표정", back[0].Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMissingFileReturnsEmpty()
    {
        Assert.Empty(PoolStore.Load(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void LoadCorruptedFileReturnsEmptyInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json ][");
            var result = PoolStore.Load(path);
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
