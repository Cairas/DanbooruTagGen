using System.Text;

namespace DanbooruTagGen.Core.Generation;

/// <summary>충돌 그룹의 적용 범위.
/// Scene: 장면 단위 — 인원과 무관하게 항상 배타(예: 낮/밤, 실내/실외, 계절).
/// Character: 캐릭터 단위 — 한 인물의 속성이라 1인일 때만 배타. 한 줄에 다인수 마커
/// (2girls, multiple_girls 등)가 있으면 서로 다른 인물의 속성일 수 있어 억제한다
/// (예: 2girls면 blue_eyes + red_eyes가 정상).</summary>
public enum ConflictScope
{
    Scene = 0,
    Character = 1,
}

/// <summary>상호배타 태그 한 그룹(이 중 둘 이상이 한 줄에 있으면 모순).
/// Except: 이 중 하나라도 한 줄에 있으면 그 그룹 규칙을 면제한다(예: heterochromia가
/// 있으면 눈 색 두 개가 정상, multicolored_hair가 있으면 머리 색 두 개가 정상).</summary>
public sealed record ConflictGroup(
    string Label,
    IReadOnlyList<string> Tags,
    ConflictScope Scope = ConflictScope.Scene,
    IReadOnlyList<string>? Except = null);

/// <summary>한 줄에서 실제로 검출된 충돌(라벨 + 같이 나온 태그들).</summary>
public sealed record ConflictHit(string Label, IReadOnlyList<string> Tags);

/// <summary>사전 점검용 슬롯 요약: 나올 수 있는 태그와, 한 줄에 최대 몇 개까지 함께 뽑히는지(MaxCount).
/// 고정 슬롯은 IsFixed=true(태그가 전부 항상 함께 나온다). 랜덤 슬롯은 인라인+풀 후보를 합친 Tags에
/// 그 슬롯의 MaxCount를 담는다 — MaxCount가 1이면 그 슬롯 안에서는 하나만 뽑히므로, 그 안의
/// 상호배타 태그끼리는 서로 충돌하지 않는다.
/// AlternativeSlot은 그룹 하나당 별도 SlotSpec을 만들고 같은 AltGroupOwnerId를 부여한다 —
/// 그룹 하나가 통째로 뽑히므로 그 그룹 안의 태그끼리는 "뽑히면 항상 같이" 나오고(Definite),
/// 같은 AltGroupOwnerId를 가진 다른 그룹의 태그와는 정확히 하나만 뽑히므로 절대 같이 못
/// 나온다(충돌 아님) — 이 구분이 없으면 그룹을 전부 합쳐 봐서 원래 같이 나올 수 없는 조합도
/// "가끔 충돌"로 오탐하게 된다.</summary>
public sealed record SlotSpec(IReadOnlyList<string> Tags, int MaxCount, bool IsFixed, int? AltGroupOwnerId = null);

public enum ConflictSeverity { Definite, Possible }

/// <summary>레시피 사전 점검 결과(항상 충돌=Definite, 가끔 가능=Possible).</summary>
public sealed record RecipeConflict(string Label, IReadOnlyList<string> Tags, ConflictSeverity Severity);

/// <summary>모순 태그 규칙. data/conflicts.csv에서 로드한다.</summary>
public sealed class ConflictRules
{
    private readonly IReadOnlyList<ConflictGroup> _groups;

    /// <summary>"이 줄은 두 명 이상"을 뜻하는 마커. 캐릭터 단위 충돌을 억제하는 데 쓴다.</summary>
    private static readonly IReadOnlySet<string> MultiPersonMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "2girls", "3girls", "4girls", "5girls", "6+girls", "multiple_girls",
        "2boys", "3boys", "4boys", "5boys", "6+boys", "multiple_boys",
        "multiple_others", "everyone",
    };

    public ConflictRules(IEnumerable<ConflictGroup> groups) => _groups = groups.ToList();
    public static ConflictRules Empty { get; } = new(Array.Empty<ConflictGroup>());
    public IReadOnlyList<ConflictGroup> Groups => _groups;
    public int Count => _groups.Count;

    /// <summary>conflicts.csv 로드. 줄 형식: "라벨|tag1,tag2,...[|scope][|except1,except2]".
    /// scope: character/캐릭터 → 캐릭터 단위(그 외/생략 → 장면 단위).
    /// except: 있으면 그 그룹을 면제하는 태그들. '#' 주석/빈 줄 무시.</summary>
    public static ConflictRules LoadFromFile(string path)
    {
        if (!File.Exists(path)) return Empty;
        var groups = new List<ConflictGroup>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split('|');
            string label = parts.Length >= 2 ? parts[0].Trim() : "충돌";
            string tagsField = parts.Length >= 2 ? parts[1] : parts[0];

            var tags = tagsField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tags.Length < 2) continue;

            var scope = parts.Length >= 3 ? ParseScope(parts[2]) : ConflictScope.Scene;
            var except = parts.Length >= 4
                ? parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();

            groups.Add(new ConflictGroup(label, tags, scope, except));
        }
        return new ConflictRules(groups);
    }

    private static ConflictScope ParseScope(string token) =>
        token.Trim().ToLowerInvariant() is "character" or "char" or "캐릭터"
            ? ConflictScope.Character
            : ConflictScope.Scene;

    /// <summary>그룹이 이 줄에서 비활성인가(억제 대상). 캐릭터 단위인데 다인수 마커가 있거나,
    /// 면제 태그(Except)가 하나라도 있으면 비활성.</summary>
    private static bool IsSuppressed(ConflictGroup g, HashSet<string> set)
    {
        if (g.Scope == ConflictScope.Character && set.Overlaps(MultiPersonMarkers)) return true;
        if (g.Except is { Count: > 0 } && g.Except.Any(set.Contains)) return true;
        return false;
    }

    /// <summary>한 줄(태그 집합)에서 같은 그룹 멤버가 2개 이상이면 충돌로 검출(억제 그룹 제외).</summary>
    public IReadOnlyList<ConflictHit> Detect(IEnumerable<string> tags)
    {
        var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        var hits = new List<ConflictHit>();
        foreach (var g in _groups)
        {
            if (IsSuppressed(g, set)) continue;
            var present = g.Tags.Where(set.Contains).ToList();
            if (present.Count >= 2) hits.Add(new ConflictHit(g.Label, present));
        }
        return hits;
    }

    public bool HasConflict(IEnumerable<string> tags)
    {
        var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        foreach (var g in _groups)
        {
            if (IsSuppressed(g, set)) continue;
            int c = 0;
            foreach (var t in g.Tags) if (set.Contains(t) && ++c >= 2) return true;
        }
        return false;
    }

    /// <summary>레시피 사전 점검. always는 항상 함께 나오는 태그(모든 고정 슬롯),
    /// reachable은 풀 후보까지 포함해 나올 수 있는 모든 태그.
    /// always에 2개 이상이면 Definite(항상 충돌), reachable에 2개 이상이면 Possible.</summary>
    public IReadOnlyList<RecipeConflict> AnalyzeRecipe(IEnumerable<string> alwaysTags, IEnumerable<string> reachableTags)
    {
        var always = new HashSet<string>(alwaysTags, StringComparer.OrdinalIgnoreCase);
        var reachable = new HashSet<string>(reachableTags, StringComparer.OrdinalIgnoreCase);
        var result = new List<RecipeConflict>();
        foreach (var g in _groups)
        {
            var inAlways = g.Tags.Where(always.Contains).ToList();
            if (inAlways.Count >= 2)
            {
                result.Add(new RecipeConflict(g.Label, inAlways, ConflictSeverity.Definite));
                continue;
            }
            var inReach = g.Tags.Where(reachable.Contains).ToList();
            if (inReach.Count >= 2)
                result.Add(new RecipeConflict(g.Label, inReach, ConflictSeverity.Possible));
        }
        return result;
    }

    /// <summary>슬롯 단위 사전 점검(권장). 같은 랜덤 슬롯에서 나오는 태그들은 그 슬롯의 MaxCount가
    /// 2 이상일 때만 서로 공존할 수 있다 — MaxCount=1이면 하나만 뽑히므로 그 풀 안의 상호배타
    /// 태그(예: 한 '시선' 풀 안의 looking_up/looking_down)는 충돌로 보지 않는다. 두 태그가
    ///   • 고정끼리 2개 이상        → Definite(항상 충돌)
    ///   • 서로 다른 출처(고정↔슬롯, 슬롯↔슬롯) 또는 같은 슬롯의 MaxCount≥2 → Possible(가끔)
    /// 억제(캐릭터 범위 다인수 / except)는 고정 태그를 기준으로 판정한다.</summary>
    public IReadOnlyList<RecipeConflict> AnalyzeRecipe(IReadOnlyList<SlotSpec> slots)
    {
        var fixedTags = new HashSet<string>(
            slots.Where(s => s.IsFixed).SelectMany(s => s.Tags), StringComparer.OrdinalIgnoreCase);
        var randomSlots = slots.Where(s => !s.IsFixed)
            .Select(s => (Set: new HashSet<string>(s.Tags, StringComparer.OrdinalIgnoreCase),
                          Multi: s.MaxCount >= 2, s.AltGroupOwnerId))
            .ToList();

        var result = new List<RecipeConflict>();
        foreach (var g in _groups)
        {
            if (g.Scope == ConflictScope.Character && fixedTags.Overlaps(MultiPersonMarkers)) continue;
            if (g.Except is { Count: > 0 } && g.Except.Any(fixedTags.Contains)) continue;

            var inFixed = g.Tags.Where(fixedTags.Contains).ToList();
            if (inFixed.Count >= 2)
            {
                result.Add(new RecipeConflict(g.Label, inFixed, ConflictSeverity.Definite));
                continue;
            }

            // 각 랜덤 슬롯에 들어 있는 이 그룹의 멤버
            var perSlot = randomSlots.Select(rs => g.Tags.Where(rs.Set.Contains).ToList()).ToList();
            var involved = new HashSet<string>(inFixed, StringComparer.OrdinalIgnoreCase);
            bool possible = false;
            bool definite = false;

            // ALT 그룹 하나 안에서 멤버 2개 이상 → 그 그룹이 뽑히면 항상 같이 나온다 → Definite
            for (int i = 0; i < perSlot.Count; i++)
                if (randomSlots[i].AltGroupOwnerId != null && perSlot[i].Count >= 2)
                {
                    definite = true;
                    foreach (var t in perSlot[i]) involved.Add(t);
                }

            // 고정 멤버(≥1) + 어떤 랜덤 슬롯 멤버(≥1)  → 공존 가능
            if (inFixed.Count >= 1)
                for (int i = 0; i < perSlot.Count; i++)
                    if (perSlot[i].Count >= 1)
                    {
                        possible = true;
                        foreach (var t in perSlot[i]) involved.Add(t);
                    }

            // 서로 다른 두 랜덤 슬롯 → 공존 가능. 단, 같은 AltGroupOwnerId를 가진 두 슬롯은
            // 같은 ALT 슬롯의 서로 다른 그룹 — 정확히 하나만 뽑히므로 절대 같이 못 나온다.
            for (int i = 0; i < perSlot.Count; i++)
                for (int j = i + 1; j < perSlot.Count; j++)
                {
                    if (perSlot[i].Count == 0 || perSlot[j].Count == 0) continue;
                    var ownerI = randomSlots[i].AltGroupOwnerId;
                    var ownerJ = randomSlots[j].AltGroupOwnerId;
                    if (ownerI != null && ownerI == ownerJ) continue;
                    possible = true;
                    foreach (var t in perSlot[i]) involved.Add(t);
                    foreach (var t in perSlot[j]) involved.Add(t);
                }

            // 같은 랜덤 풀 슬롯에서 멤버 2개 이상 + MaxCount≥2 → 공존 가능(ALT 그룹은 위에서 처리 완료)
            for (int i = 0; i < perSlot.Count; i++)
                if (randomSlots[i].AltGroupOwnerId == null && randomSlots[i].Multi && perSlot[i].Count >= 2)
                {
                    possible = true;
                    foreach (var t in perSlot[i]) involved.Add(t);
                }

            if (definite && involved.Count >= 2)
                result.Add(new RecipeConflict(g.Label, involved.ToList(), ConflictSeverity.Definite));
            else if (possible && involved.Count >= 2)
                result.Add(new RecipeConflict(g.Label, involved.ToList(), ConflictSeverity.Possible));
        }
        return result;
    }
}
