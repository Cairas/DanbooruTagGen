using System.IO.Compression;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public sealed class BackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtg_backup_" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    private string Backups => Path.Combine(_root, "backups");
    private static readonly DateTime Now = new(2026, 11, 20, 15, 30, 12);

    public BackupStoreTests()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "recipes.json"), "[\"v1\"]");
        File.WriteAllText(Path.Combine(Data, "settings.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(Data, "unrelated.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void DailyBackupIsCreatedOncePerDay()
    {
        var first = BackupStore.CreateDailyIfMissing(Data, Backups, Now);
        Assert.NotNull(first);
        Assert.Null(BackupStore.CreateDailyIfMissing(Data, Backups, Now.AddHours(3)));
        Assert.NotNull(BackupStore.CreateDailyIfMissing(Data, Backups, Now.AddDays(1)));
        Assert.Equal(new[] { "recipes.json", "settings.json" }, BackupStore.Contents(first!).OrderBy(x => x));
    }

    [Fact]
    public void RestoreBringsBackOnlyBackedUpFiles()
    {
        var snap = BackupStore.CreateSnapshot(Data, Backups, "프리셋 갱신 전", Now)!;
        File.WriteAllText(Path.Combine(Data, "recipes.json"), "[\"v2-broken\"]");
        File.WriteAllText(Path.Combine(Data, "hooks.json"), "[]");   // 백업 뒤에 생긴 파일 — 그대로 둔다

        Assert.Equal(2, BackupStore.Restore(snap, Data));
        Assert.Equal("[\"v1\"]", File.ReadAllText(Path.Combine(Data, "recipes.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(Data, "hooks.json")));
        Assert.Empty(Directory.GetFiles(Data, "*.restore-tmp"));
    }

    [Fact]
    public void ListShowsNewestFirstWithLabels()
    {
        BackupStore.CreateDailyIfMissing(Data, Backups, Now.AddDays(-1));
        BackupStore.CreateSnapshot(Data, Backups, "복원 전", Now);
        var list = BackupStore.List(Backups);
        Assert.Equal(2, list.Count);
        Assert.False(list[0].IsDaily);
        Assert.Equal("복원 전", list[0].Label);
        Assert.Equal(Now, list[0].Time);
        Assert.True(list[1].IsDaily);
    }

    [Fact]
    public void PruneKeepsTheNewest()
    {
        for (int d = 0; d < BackupStore.KeepDaily + 3; d++) BackupStore.CreateDailyIfMissing(Data, Backups, Now.AddDays(-d));
        for (int s = 0; s < BackupStore.KeepSnapshots + 2; s++) BackupStore.CreateSnapshot(Data, Backups, "x", Now.AddMinutes(-s));
        Assert.Equal(5, BackupStore.Prune(Backups));
        var list = BackupStore.List(Backups);
        Assert.Equal(BackupStore.KeepDaily, list.Count(e => e.IsDaily));
        Assert.Equal(BackupStore.KeepSnapshots, list.Count(e => !e.IsDaily));
        Assert.Contains(list, e => e.IsDaily && e.Path.Contains(Now.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void RestoreIgnoresEntriesOutsideTheWhitelist()
    {
        Directory.CreateDirectory(Backups);
        var evil = Path.Combine(Backups, "snap_2026-11-20_000000_evil.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("../escape.json").Open())) w.Write("bad");
            using (var w = new StreamWriter(zip.CreateEntry("recipes.json").Open())) w.Write("[\"ok\"]");
        }
        Assert.Equal(1, BackupStore.Restore(evil, Data));
        Assert.False(File.Exists(Path.Combine(_root, "escape.json")));
        Assert.Equal("[\"ok\"]", File.ReadAllText(Path.Combine(Data, "recipes.json")));
    }
}
