using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>PoolStore/RecipeLibraryStore/SettingsStore가 공통으로 위임하는 저장/불러오기
/// 헬퍼 자체를 검증한다. 개별 스토어 테스트가 이미 라운드트립을 확인하지만, "손상 파일이면
/// 기본값으로 폴백하고 .bak을 남긴다"는 계약은 여기서 한 번만 확인하면 충분하다.</summary>
public class JsonStoreTests
{
    private sealed record Sample(string Name, int Value);

    [Fact]
    public void SaveAtomicThenLoadOrDefaultRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.json");
        try
        {
            JsonStore.SaveAtomic(new Sample("x", 1), path);
            var back = JsonStore.LoadOrDefault(path, new Sample("fallback", 0));
            Assert.Equal("x", back.Name);
            Assert.Equal(1, back.Value);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveAtomicOverwritesExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.json");
        try
        {
            JsonStore.SaveAtomic(new Sample("first", 1), path);
            JsonStore.SaveAtomic(new Sample("second", 2), path);
            var back = JsonStore.LoadOrDefault(path, new Sample("fallback", 0));
            Assert.Equal("second", back.Name);
            Assert.False(File.Exists(path + ".tmp"), "임시 파일은 교체 후 남아있으면 안 된다");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadOrDefaultReturnsFallbackForMissingFile()
    {
        var fallback = new Sample("fallback", 0);
        var result = JsonStore.LoadOrDefault(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"), fallback);
        Assert.Same(fallback, result);
    }

    [Fact]
    public void LoadOrDefaultBacksUpAndFallsBackOnCorruptedJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json at all {{{");
            var fallback = new Sample("fallback", 0);
            var result = JsonStore.LoadOrDefault(path, fallback);
            Assert.Same(fallback, result);
            Assert.True(File.Exists(path + ".bak"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public async Task LoadOrDefaultRetriesWhileFileIsBrieflyLocked()
    {
        // FileSystemWatcher가 "쓰는 중"인 파일을 읽으러 들어오는 실제 경로의 회귀 테스트.
        // 예전엔 IOException이 그대로 위로 새어 앱 시작/프리셋 자동 갱신이 통째로 죽었다.
        var path = Path.Combine(Path.GetTempPath(), $"locked_{Guid.NewGuid():N}.json");
        JsonStore.SaveAtomic(new Sample("written", 7), path);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var unlock = Task.Run(async () =>
        {
            await Task.Delay(120);   // 재시도 창(3회 × 80ms) 안에서 잠금이 풀린다
            stream.Dispose();
        });
        try
        {
            var result = JsonStore.LoadOrDefault(path, new Sample("fallback", 0));
            Assert.Equal("written", result.Name);
        }
        finally
        {
            await unlock;
            stream.Dispose();
            File.Delete(path);
        }
    }
}
