using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>생성 기록 하나에 든 레시피별 결과(품질 태그 붙이기 전 원문 + 중복 판정 키 — 다시 띄운 뒤 잠금·다시 뽑기가 그대로 되게).</summary>
public sealed class GenerationRecordRecipe
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LockKey { get; set; } = "";
    public int Requested { get; set; }
    public List<string> Lines { get; set; } = new();
    public List<string> LineKeys { get; set; } = new();
}

/// <summary>"생성"(파일 쓰기) 한 번의 기록.</summary>
public sealed class GenerationRecord
{
    public DateTime At { get; set; }
    public int Seed { get; set; }
    public string OutputPath { get; set; } = "";
    public string Mode { get; set; } = "";
    public string OptionsSummary { get; set; } = "";
    public List<GenerationRecordRecipe> Recipes { get; set; } = new();
}

/// <summary>목록용 요약(파일 이름에서 — 기록을 다 풀지 않고 목록을 띄운다).</summary>
public sealed record GenerationRecordEntry(string Path, DateTime At, string Summary);

/// <summary>생성 기록 저장소: <c>{dir}/yyyyMMdd_HHmmss_요약.json.gz</c>, 최근 <see cref="Keep"/>개.</summary>
public static class GenerationHistoryStore
{
    public const int Keep = 30;
    private const string StampFormat = "yyyyMMdd_HHmmss";

    public static string Save(string dir, GenerationRecord record)
    {
        Directory.CreateDirectory(dir);
        int lines = record.Recipes.Sum(r => r.Lines.Count);
        var first = record.Recipes.FirstOrDefault()?.Name ?? "";
        var summary = record.Recipes.Count == 1 ? $"{Shorten(first, 40)} {lines}줄" : $"{record.Recipes.Count}개 레시피 {lines}줄";
        var safe = new string(summary.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '_' ? ' ' : c).ToArray()).Trim();
        var stamp = record.At.ToString(StampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, $"{stamp}_{safe}.json.gz");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{stamp}_{safe} ({n}).json.gz");

        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gz, record, JsonStore.Options);
        File.Move(tmp, path, overwrite: true);

        foreach (var old in List(dir).Skip(Keep))
        {
            try { File.Delete(old.Path); }
            catch (IOException) { /* 다음에 다시 지운다 */ }
        }
        return path;
    }

    public static IReadOnlyList<GenerationRecordEntry> List(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<GenerationRecordEntry>();
        var list = new List<GenerationRecordEntry>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json.gz"))
        {
            var name = Path.GetFileName(path)[..^".json.gz".Length];
            if (name.Length < StampFormat.Length
                || !DateTime.TryParseExact(name[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                continue;
            list.Add(new GenerationRecordEntry(path, at, name.Length > StampFormat.Length + 1 ? name[(StampFormat.Length + 1)..] : ""));
        }
        return list.OrderByDescending(e => e.At).ThenByDescending(e => e.Path, StringComparer.Ordinal).ToList();
    }

    public static GenerationRecord? Load(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<GenerationRecord>(gz, JsonStore.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return null; }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
