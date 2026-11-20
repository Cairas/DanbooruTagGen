using System.Globalization;
using System.IO.Compression;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>백업 하나. <see cref="IsDaily"/>: 그날 첫 실행 때 자동으로 뜬 백업, 아니면 위험한 작업(프리셋 갱신·제거·복원) 직전 스냅샷.</summary>
public sealed record BackupEntry(string Path, DateTime Time, string Label, bool IsDaily, long Bytes);

/// <summary>사용자 데이터(%APPDATA%\DanbooruTagGen의 레시피·풀·훅·설정 등)를 zip으로 백업·복원한다.
/// <para>왜: 지금까지는 손상 시 .bak 하나만 남았다. 시딩 경합·잘못된 프리셋 갱신으로 레시피가 사라지면 되돌릴 시점이 없었다.
/// 일자별 백업(그날 첫 실행, 시딩 전)과 위험 작업 직전 스냅샷을 남기고, 오래된 것은 지운다.</para>
/// <para>settings.json은 레시피와 같이 백업·복원해야 한다 — 시딩 장부(seededIds)가 레시피 목록과 어긋나면 유령 시딩이 난다.</para></summary>
public static class BackupStore
{
    /// <summary>백업 대상(데이터 폴더 안 파일 이름). 이 목록 밖의 이름은 복원 때도 절대 쓰지 않는다(zip 안 경로 조작 방지).</summary>
    public static readonly IReadOnlyList<string> Files = new[]
    {
        "settings.json", "pools.json", "recipes.json", "hooks.json", "batch-presets.json", "last-recipe.json",
    };

    public const int KeepDaily = 14;
    public const int KeepSnapshots = 10;

    private const string DailyPrefix = "daily_";
    private const string SnapshotPrefix = "snap_";
    private const string DayFormat = "yyyy-MM-dd";
    private const string StampFormat = "yyyy-MM-dd_HHmmss";

    /// <summary>오늘 날짜 백업이 없으면 만든다. 반환: 만든 파일 경로(이미 있었거나 백업할 파일이 없으면 null).</summary>
    public static string? CreateDailyIfMissing(string dataDir, string backupDir, DateTime now)
    {
        var path = Path.Combine(backupDir, DailyPrefix + now.ToString(DayFormat, CultureInfo.InvariantCulture) + ".zip");
        if (File.Exists(path)) return null;
        return Write(dataDir, path) ? path : null;
    }

    /// <summary>지금 상태의 스냅샷. <paramref name="label"/>은 목록에 보일 짧은 이유(예: "프리셋 갱신 전").</summary>
    public static string? CreateSnapshot(string dataDir, string backupDir, string label, DateTime now)
    {
        var safe = new string(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '_' ? ' ' : c).ToArray()).Trim();
        var path = Path.Combine(backupDir, SnapshotPrefix + now.ToString(StampFormat, CultureInfo.InvariantCulture) + "_" + safe + ".zip");
        for (int n = 2; File.Exists(path); n++)   // 같은 초에 두 번
            path = Path.Combine(backupDir, $"{SnapshotPrefix}{now.ToString(StampFormat, CultureInfo.InvariantCulture)}_{safe} ({n}).zip");
        return Write(dataDir, path) ? path : null;
    }

    /// <summary>임시 파일에 다 쓴 뒤 이름을 바꾼다 — 쓰다 죽어도 반쪽짜리 백업이 목록에 안 남는다.</summary>
    private static bool Write(string dataDir, string path)
    {
        var present = Files.Select(f => Path.Combine(dataDir, f)).Where(File.Exists).ToList();
        if (present.Count == 0) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                foreach (var file in present)
                    zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>백업 목록, 최신순.</summary>
    public static IReadOnlyList<BackupEntry> List(string backupDir)
    {
        if (!Directory.Exists(backupDir)) return Array.Empty<BackupEntry>();
        var list = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(backupDir, "*.zip"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith(DailyPrefix, StringComparison.Ordinal)
                && DateTime.TryParseExact(name[DailyPrefix.Length..], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                // 일자 백업의 실제 시각은 파일을 만든 시각(그날 첫 실행).
                list.Add(new BackupEntry(path, File.GetLastWriteTime(path) is var t && t.Date == day ? t : day, "그날 첫 실행(자동)", true, new FileInfo(path).Length));
            }
            else if (name.StartsWith(SnapshotPrefix, StringComparison.Ordinal) && name.Length >= SnapshotPrefix.Length + StampFormat.Length
                     && DateTime.TryParseExact(name.Substring(SnapshotPrefix.Length, StampFormat.Length), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                var label = name.Length > SnapshotPrefix.Length + StampFormat.Length + 1 ? name[(SnapshotPrefix.Length + StampFormat.Length + 1)..] : "스냅샷";
                list.Add(new BackupEntry(path, at, label, false, new FileInfo(path).Length));
            }
        }
        return list.OrderByDescending(e => e.Time).ToList();
    }

    /// <summary>백업에 든 파일 이름(목록 표시용).</summary>
    public static IReadOnlyList<string> Contents(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).Where(n => Files.Contains(n)).ToList();
    }

    /// <summary>백업을 데이터 폴더에 되돌린다. 백업에 든 파일만 바꾸고(없는 파일은 지금 것을 그대로 둔다), 파일마다 임시 이름으로
    /// 풀었다가 바꿔 끼운다. 대상 목록(<see cref="Files"/>) 밖의 항목은 무시한다. 반환: 되돌린 파일 수.</summary>
    public static int Restore(string zipPath, string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        using var zip = ZipFile.OpenRead(zipPath);
        int n = 0;
        foreach (var entry in zip.Entries)
        {
            if (!Files.Contains(entry.FullName)) continue;
            var target = Path.Combine(dataDir, entry.FullName);
            var tmp = target + ".restore-tmp";
            entry.ExtractToFile(tmp, overwrite: true);
            File.Move(tmp, target, overwrite: true);
            n++;
        }
        return n;
    }

    /// <summary>일자 백업은 최근 <see cref="KeepDaily"/>개, 스냅샷은 최근 <see cref="KeepSnapshots"/>개만 남긴다. 반환: 지운 수.</summary>
    public static int Prune(string backupDir)
    {
        var all = List(backupDir);
        var doomed = all.Where(e => e.IsDaily).Skip(KeepDaily)
            .Concat(all.Where(e => !e.IsDaily).Skip(KeepSnapshots))
            .ToList();
        foreach (var e in doomed)
        {
            try { File.Delete(e.Path); }
            catch (IOException) { /* 다음 실행 때 다시 지운다 */ }
        }
        return doomed.Count;
    }
}
