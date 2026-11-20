using System.Globalization;
using System.Text.Json;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>레시피 버전 하나(저장된 JSON 파일).</summary>
public sealed record RecipeVersion(string Path, DateTime Time, string Reason);

/// <summary>레시피별 버전 기록. <c>{historyDir}/{레시피 id}/yyyyMMdd_HHmmss_이유.json</c>.
/// <para>왜: 덮어쓰면 옛 버전이 사라져서, 확장할 때마다 새 id로 복사해 두는 우회를 썼다(목록 순서·시딩 장부까지 꼬였다).
/// 열 때(기준)·고칠 때(편집)·프리셋 갱신으로 바뀌기 직전 내용을 남겨, 되돌리기·비교를 레시피 안에서 한다.</para>
/// <para>같은 내용은 다시 남기지 않고, 짧은 시간 안의 연속 편집은 마지막 버전 하나로 합친다(태그 하나 고칠 때마다 버전이 쌓이지 않게).</para></summary>
public static class RecipeHistoryStore
{
    public const int KeepPerRecipe = 30;
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(5);
    public const string EditReason = "편집";
    private const string StampFormat = "yyyyMMdd_HHmmss";

    private static string DirOf(string historyDir, string recipeId)
    {
        var safe = new string(recipeId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return Path.Combine(historyDir, safe);
    }

    /// <summary>버전 목록, 최신순.</summary>
    public static IReadOnlyList<RecipeVersion> List(string historyDir, string recipeId)
    {
        var dir = DirOf(historyDir, recipeId);
        if (!Directory.Exists(dir)) return Array.Empty<RecipeVersion>();
        var list = new List<RecipeVersion>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < StampFormat.Length
                || !DateTime.TryParseExact(name[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                continue;
            var reason = name.Length > StampFormat.Length + 1 ? name[(StampFormat.Length + 1)..] : "";
            list.Add(new RecipeVersion(path, t, reason));
        }
        return list.OrderByDescending(v => v.Time).ThenByDescending(v => v.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>지금 내용을 버전으로 남긴다. 최신 버전과 내용이 같으면 아무것도 안 한다. 이유가 "편집"이고 최신 버전도 "편집"이며
    /// <see cref="CoalesceWindow"/> 안이면 그 버전을 이 내용으로 바꾼다(합치기). 반환: 무언가 썼는가.</summary>
    public static bool Record(string historyDir, Recipe recipe, string reason, DateTime now) =>
        Record(historyDir, Serialize(recipe), recipe.Id, reason, now);

    /// <summary>버전 파일과 같은 방식으로 직렬화한다 — 선택 시 기준을 메모리에만 잡아 둘 때 쓴다.</summary>
    public static string Serialize(Recipe recipe) => JsonSerializer.Serialize(recipe, JsonStore.Options);

    /// <summary>미리 직렬화해 둔 내용(<see cref="Serialize"/>)을 버전으로 남긴다. 규칙은 Recipe 오버로드와 같다.</summary>
    public static bool Record(string historyDir, string json, string recipeId, string reason, DateTime now)
    {
        var versions = List(historyDir, recipeId);
        if (versions.Count > 0)
        {
            var latest = versions[0];
            string? latestJson = null;
            try { latestJson = File.ReadAllText(latest.Path); }
            catch (IOException) { /* 못 읽으면 새로 남긴다 */ }
            if (latestJson == json) return false;
            if (reason == EditReason && latest.Reason == EditReason && now - latest.Time < CoalesceWindow)
            {
                WriteAtomic(latest.Path, json);
                return true;
            }
        }

        var dir = DirOf(historyDir, recipeId);
        Directory.CreateDirectory(dir);
        var safeReason = new string(reason.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
        var path = Path.Combine(dir, $"{now.ToString(StampFormat, CultureInfo.InvariantCulture)}_{safeReason}.json");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(dir, $"{now.ToString(StampFormat, CultureInfo.InvariantCulture)}_{safeReason} ({n}).json");
        WriteAtomic(path, json);

        foreach (var old in List(historyDir, recipeId).Skip(KeepPerRecipe))
        {
            try { File.Delete(old.Path); }
            catch (IOException) { /* 다음에 다시 지운다 */ }
        }
        return true;
    }

    public static Recipe? Load(RecipeVersion version) => JsonStore.LoadOrDefault<Recipe?>(version.Path, null);

    /// <summary>"열 때" 남기는 기준 버전의 이유. 최근 수정 시각을 셀 때는 뺀다(열어 보기만 한 것은 수정이 아니다).</summary>
    public const string BaselineReason = "기준";

    /// <summary>레시피를 마지막으로 고친 시각(정렬 "최근 수정순"용 — 기준 버전 제외). 없으면 null.</summary>
    public static DateTime? LastChanged(string historyDir, string recipeId) =>
        List(historyDir, recipeId).FirstOrDefault(v => v.Reason != BaselineReason)?.Time;

    private static void WriteAtomic(string path, string json)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
