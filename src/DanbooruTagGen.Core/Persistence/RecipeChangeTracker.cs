using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

public enum RecipeChangeKind { New, Updated }

/// <summary>프리셋 갱신으로 새로 들어왔거나 내용이 바뀐 레시피 표시(목록의 🆕/✏️ 배지). 사용자가 그 레시피를 열어 보거나
/// "모두 확인"을 누르면 지운다. 오래된 표시는 <see cref="RecipeChangeTracker.MaxAge"/>가 지나면 저절로 지운다.</summary>
public sealed class RecipeChangeMark
{
    public RecipeChangeKind Kind { get; set; } = RecipeChangeKind.New;
    public DateTime At { get; set; } = DateTime.Now;
}

public static class RecipeChangeTracker
{
    /// <summary>이 기간이 지난 표시는 지운다 — 몇 주 전에 들어온 팩이 계속 🆕로 남으면 표시가 의미를 잃는다.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    /// <summary>갱신 전 id 목록에 없던 레시피를 🆕로 표시한다. 반환: 새로 표시한 개수.</summary>
    public static int MarkNew(IDictionary<string, RecipeChangeMark> marks, IEnumerable<string> idsBefore,
        IEnumerable<Recipe> recipesAfter, DateTime now)
    {
        var before = idsBefore.ToHashSet(StringComparer.Ordinal);
        int n = 0;
        foreach (var r in recipesAfter)
        {
            if (before.Contains(r.Id)) continue;
            marks[r.Id] = new RecipeChangeMark { Kind = RecipeChangeKind.New, At = now };
            n++;
        }
        return n;
    }

    /// <summary>내용이 바뀐 레시피를 ✏️로 표시한다(아직 확인 안 한 🆕는 🆕 그대로 — 새로 온 팩이 곧바로 고쳐져도 "새 팩"이 더 중요한 정보다).
    /// 반환: 표시한 개수.</summary>
    public static int MarkUpdated(IDictionary<string, RecipeChangeMark> marks, IEnumerable<string> ids, DateTime now)
    {
        int n = 0;
        foreach (var id in ids)
        {
            if (marks.TryGetValue(id, out var m) && m.Kind == RecipeChangeKind.New) { m.At = now; n++; continue; }
            marks[id] = new RecipeChangeMark { Kind = RecipeChangeKind.Updated, At = now };
            n++;
        }
        return n;
    }

    /// <summary>없어진 레시피와 오래된 표시를 지운다. 반환: 지운 개수.</summary>
    public static int Prune(IDictionary<string, RecipeChangeMark> marks, IEnumerable<string> existingIds, DateTime now)
    {
        var exist = existingIds.ToHashSet(StringComparer.Ordinal);
        var stale = marks.Where(kv => !exist.Contains(kv.Key) || now - kv.Value.At > MaxAge).Select(kv => kv.Key).ToList();
        foreach (var id in stale) marks.Remove(id);
        return stale.Count;
    }

    public static string Badge(IReadOnlyDictionary<string, RecipeChangeMark> marks, string id) =>
        marks.TryGetValue(id, out var m) ? (m.Kind == RecipeChangeKind.New ? "🆕" : "✏️") : "";
}
