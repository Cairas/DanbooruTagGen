namespace DanbooruTagGen.Core.Logging;

/// <summary>같은 문제를 하루에 한 번만 로그에 남기게 거른다(앱이 켜져 있는 동안 기억). 같은 배치를 여러 번 생성하거나
/// 같은 오류가 되풀이될 때 로그가 같은 항목으로 뒤덮이지 않게 — 로그는 "무슨 문제가 있었나"를 찾는 곳이다.</summary>
public sealed class RepeatFilter
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private DateTime _day = DateTime.MinValue;

    /// <summary>오늘 처음 보는 열쇠면 true(기억해 둔다). 날이 바뀌면 다 잊는다.</summary>
    public bool IsNew(string key, DateTime now)
    {
        lock (_gate)
        {
            if (now.Date != _day) { _seen.Clear(); _day = now.Date; }
            return _seen.Add(key);
        }
    }
}
