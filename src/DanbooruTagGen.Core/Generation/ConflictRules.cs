using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

/// <summary>규칙이 "충돌"을 정의하는 방식.
/// Exclusive: 멤버 전원 상호배타(기존 문법 — 둘 이상 같이 있으면 충돌).
/// Sides: 편 나눔(<c>A1,A2 vs B1,B2</c>) — 서로 다른 편의 멤버가 같이 있으면 충돌, 같은 편끼리는 괜찮다.
/// Budget: 자원 예산(<c>@hands:2</c>) — 같이 있는 멤버의 비용 합이 예산을 넘으면 충돌.</summary>
public enum ConflictKind
{
    Exclusive = 0,
    Sides = 1,
    Budget = 2,
}

/// <summary>자원 예산 규칙의 자원 이름·예산·멤버별 비용(<see cref="ConflictGroup.Tags"/>와 같은 순서).</summary>
public sealed record ResourceBudget(string Resource, int Limit, IReadOnlyList<int> Costs);

/// <summary>모순 규칙 하나. 기본은 상호배타 태그 한 그룹(이 중 둘 이상이 한 줄에 있으면 모순).
/// Except: 이 중 하나라도 한 줄에 있으면 그 그룹 규칙을 면제한다(예: heterochromia가
/// 있으면 눈 색 두 개가 정상, multicolored_hair가 있으면 머리 색 두 개가 정상).
/// <see cref="Sides"/>나 <see cref="Budget"/>이 있으면 판정 방식이 바뀐다(<see cref="Kind"/>).
/// 두 속성을 위치 인자가 아닌 init으로 둔 이유: 기존 4-인자 생성(테스트·호출부)을 그대로 두기 위해서다.</summary>
public sealed record ConflictGroup(
    string Label,
    IReadOnlyList<string> Tags,
    ConflictScope Scope = ConflictScope.Scene,
    IReadOnlyList<string>? Except = null)
{
    /// <summary>편 나눔. 각 편은 <see cref="Tags"/>의 부분집합이고 편끼리 겹치지 않는다. null이면 상호배타.</summary>
    public IReadOnlyList<IReadOnlyList<string>>? Sides { get; init; }

    /// <summary>자원 예산. null이면 예산 규칙이 아니다.</summary>
    public ResourceBudget? Budget { get; init; }

    public ConflictKind Kind => Budget is not null ? ConflictKind.Budget
        : Sides is not null ? ConflictKind.Sides
        : ConflictKind.Exclusive;

    /// <summary>편 나눔 규칙. 예: 쾌락 표정 편 vs 공포 표정 편.</summary>
    public static ConflictGroup Bipartite(
        string label, IReadOnlyList<IReadOnlyList<string>> sides,
        ConflictScope scope = ConflictScope.Scene, IReadOnlyList<string>? except = null) =>
        new(label, sides.SelectMany(s => s).ToList(), scope, except) { Sides = sides };

    /// <summary>자원 예산 규칙. 예: 그의 양손(예산 2) — full_nelson=2, grabbing_another's_breast=1, …</summary>
    public static ConflictGroup WithBudget(
        string label, string resource, int limit, IReadOnlyList<(string Tag, int Cost)> members,
        ConflictScope scope = ConflictScope.Scene, IReadOnlyList<string>? except = null) =>
        new(label, members.Select(m => m.Tag).ToList(), scope, except)
        {
            Budget = new ResourceBudget(resource, limit, members.Select(m => m.Cost).ToList()),
        };
}

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
public sealed record SlotSpec(IReadOnlyList<string> Tags, int MaxCount, bool IsFixed, int? AltGroupOwnerId = null)
{
    /// <summary>레시피 슬롯들을 <see cref="ConflictRules.AnalyzeRecipe(IReadOnlyList{SlotSpec})"/>용 요약으로 바꾼다.
    /// 레시피 빌더의 실시간 경고와 라이브러리 배지가 같은 기준을 쓰도록 한 곳에 둔다(예전엔 두 화면에 복사본이
    /// 따로 있었다). 생성기와 같은 기준으로:
    /// <list type="bullet">
    /// <item>꺼 둔 슬롯은 출력되지 않으므로 뺀다.</item>
    /// <item>랜덤 풀 후보는 <see cref="WildcardGenerator.ResolveCandidates"/>(인라인 + 풀 + 체이닝, 중복 제거) 그대로.</item>
    /// <item>가중치 0인 ALT 그룹은 절대 안 뽑히므로 뺀다 — <c>tools/conflict_scan.py</c>도 같은 기준이다. 넣으면 "없음"
    /// 분기 같은 비활성 그룹이 일으키지도 않는 모순을 "가끔"/"항상"으로 오탐한다.</item>
    /// </list></summary>
    public static IReadOnlyList<SlotSpec> FromSlots(IEnumerable<Models.Slot> slots, IReadOnlyDictionary<string, Models.Pool> poolsById)
    {
        var specs = new List<SlotSpec>();
        int nextAltOwnerId = 0;
        foreach (var slot in slots)
        {
            if (!slot.IsEnabled) continue;
            switch (slot)
            {
                case Models.FixedSlot f:
                    specs.Add(new SlotSpec(f.Tags.ToList(), f.Tags.Count, IsFixed: true));
                    break;
                case Models.RandomPoolSlot r:
                    specs.Add(new SlotSpec(WildcardGenerator.ResolveCandidates(r, poolsById), r.MaxCount, IsFixed: false));
                    break;
                case Models.AlternativeSlot alt when alt.Groups.Count > 0:
                    // 그룹 하나가 통째로 뽑혀서 다른 그룹 태그와는 절대 안 섞인다. 그룹마다 별도 SlotSpec을 만들고
                    // 같은 AltGroupOwnerId를 줘 "같은 슬롯의 다른 그룹끼리는 동시 등장 불가"를 알린다.
                    int ownerId = nextAltOwnerId++;
                    foreach (var g in alt.Groups)
                        if (g.Weight > 0)
                            specs.Add(new SlotSpec(g.Tags.ToList(), g.Tags.Count, IsFixed: false, ownerId));
                    break;
            }
        }
        return specs;
    }
}

public enum ConflictSeverity { Definite, Possible }

/// <summary>conflicts.csv에서 규칙 줄을 라벨로 찾는다(편집 화면의 "규칙 보기" — 어떤 줄 때문에 모순으로 잡혔는지).</summary>
public static class ConflictRuleSource
{
    /// <summary>라벨 칸(첫 '|' 앞, 앞뒤 공백 무시)이 <paramref name="labels"/> 중 하나와 같은 줄들(1부터 센 줄 번호와 원문).</summary>
    public static IReadOnlyList<(int Line, string Text)> FindLines(string csvPath, IEnumerable<string> labels)
    {
        var wanted = labels.Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var result = new List<(int, string)>();
        if (!File.Exists(csvPath) || wanted.Count == 0) return result;
        int n = 0;
        foreach (var line in File.ReadLines(csvPath))
        {
            n++;
            int bar = line.IndexOf('|');
            if (bar <= 0) continue;
            if (wanted.Contains(line[..bar].Trim())) result.Add((n, line));
        }
        return result;
    }
}

/// <summary>레시피 사전 점검 결과(항상 충돌=Definite, 가끔 가능=Possible).</summary>
public sealed record RecipeConflict(string Label, IReadOnlyList<string> Tags, ConflictSeverity Severity);

/// <summary>모순 태그 규칙. data/conflicts.csv에서 로드한다.
/// <para><c>tools/conflict_scan.py</c>의 load_conflict_groups/analyze_budget이 같은 문법·판정의 파이썬
/// 포팅이다 — 한쪽을 고치면 다른 쪽도 같이 고칠 것.</para></summary>
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

    /// <summary>그룹별 판정 키(<see cref="TagKey.Normalize"/>)를 미리 계산해 둔다. 줄 쪽 태그도
    /// <see cref="TagKey"/>로 분해·정규화해서 비교하므로, <c>(ahegao:1.2)</c>나
    /// <c>(문장:1.1), full_nelson</c> 같은 문자열 안의 태그도 규칙에 걸린다(예전엔 원문 통째 비교라
    /// 전부 빠져나갔다). 결과(ConflictHit/RecipeConflict)의 태그 이름은 conflicts.csv 원문을 쓴다.</summary>
    private readonly IReadOnlyList<KeyedGroup> _keyed;

    /// <summary>멤버 키 → 그 키를 멤버로 가진 규칙들(그 규칙 안에서의 편·비용과 함께). 슬롯 단위 로컬 필터
    /// (<see cref="LineTracker"/>)가 후보 하나마다 전 규칙을 훑지 않도록 — 후보가 건드리는 규칙만 본다.</summary>
    private readonly Dictionary<string, List<MemberRef>> _byMemberKey = new(StringComparer.Ordinal);

    /// <summary>면제 키 → 그 키로 면제되는 규칙들. <see cref="LineTracker"/>가 줄에 면제 태그가 들어오는 순간
    /// 해당 규칙의 억제 상태를 갱신하는 데 쓴다.</summary>
    private readonly Dictionary<string, List<KeyedGroup>> _byExceptKey = new(StringComparer.Ordinal);

    /// <summary>멤버 하나. Side: 편 번호(상호배타는 멤버마다 고유), Cost: 예산 비용(그 외 1).</summary>
    private readonly record struct Member(string Name, string Key, int Side, int Cost);

    /// <summary>역인덱스 항목: 어느 규칙의 멤버인지 + 그 규칙 안에서의 편·비용.</summary>
    private readonly record struct MemberRef(KeyedGroup Rule, string Key, int Side, int Cost);

    /// <summary>다인수 마커의 판정 키 비교용(서수). 판정 키는 이미 소문자라 <see cref="KeyView.AnyMultiPerson"/>
    /// (줄 집합의 서수 비교)과 같은 결과를 낸다.</summary>
    private static readonly HashSet<string> MultiPersonKeys = new(MultiPersonMarkers, StringComparer.Ordinal);

    /// <summary>판정에 쓰는 키 집합 보기 — 집합 두 개(지금까지 확정된 줄 + 후보)를 합치지 않고 본다.
    /// 후보 하나마다 합집합을 새로 만들면 줄 수 × 슬롯 × 후보만큼 할당이 생긴다.</summary>
    private readonly struct KeyView
    {
        private readonly HashSet<string> _a;
        private readonly HashSet<string>? _b;
        public KeyView(HashSet<string> a, HashSet<string>? b = null) { _a = a; _b = b; }
        public bool Contains(string k) => _a.Contains(k) || (_b != null && _b.Contains(k));
        public bool AnyMultiPerson()
        {
            foreach (var m in MultiPersonMarkers) if (Contains(m)) return true;
            return false;
        }
    }

    private sealed class KeyedGroup
    {
        /// <summary><see cref="_keyed"/> 안의 위치. 인덱스로 추린 규칙을 원래(파일) 순서로 되돌려 결과 순서를
        /// 전수 순회 때와 똑같이 유지하는 데 쓴다.</summary>
        public required int Index { get; init; }
        public required ConflictGroup Group { get; init; }
        public required Member[] Members { get; init; }
        public required string[] ExceptKeys { get; init; }
        public ConflictKind Kind => Group.Kind;
        public int Limit => Group.Budget?.Limit ?? 1;

        public List<string> PresentIn(HashSet<string> keys) => PresentIn(new KeyView(keys));

        public List<string> PresentIn(KeyView keys)
        {
            var present = new List<string>();
            foreach (var m in Members) if (keys.Contains(m.Key)) present.Add(m.Name);
            return present;
        }

        /// <summary>이 키 집합에서 규칙이 깨지는가(억제 여부는 따로 본다).</summary>
        public bool Violated(KeyView keys)
        {
            if (Kind == ConflictKind.Budget)
            {
                long sum = 0;
                foreach (var m in Members) if (keys.Contains(m.Key)) sum += m.Cost;
                return sum > Limit;
            }
            // 상호배타·편 나눔: 서로 다른 편이 둘 이상 있으면 충돌(상호배타는 멤버마다 편이 고유).
            int firstSide = -1;
            foreach (var m in Members)
            {
                if (!keys.Contains(m.Key)) continue;
                if (firstSide < 0) firstSide = m.Side;
                else if (m.Side != firstSide) return true;
            }
            return false;
        }

        public bool IsSuppressed(KeyView keys)
        {
            if (Group.Scope == ConflictScope.Character && keys.AnyMultiPerson()) return true;
            foreach (var e in ExceptKeys) if (keys.Contains(e)) return true;
            return false;
        }
    }

    public ConflictRules(IEnumerable<ConflictGroup> groups)
    {
        _groups = groups.ToList();
        _keyed = _groups.Select((g, i) => BuildKeyed(g, i)).ToList();
        foreach (var g in _keyed)
        {
            foreach (var m in g.Members)
            {
                if (!_byMemberKey.TryGetValue(m.Key, out var list)) _byMemberKey[m.Key] = list = new List<MemberRef>();
                list.Add(new MemberRef(g, m.Key, m.Side, m.Cost));
            }
            foreach (var e in g.ExceptKeys.Distinct(StringComparer.Ordinal))
            {
                if (!_byExceptKey.TryGetValue(e, out var list)) _byExceptKey[e] = list = new List<KeyedGroup>();
                list.Add(g);
            }
        }
    }

    /// <summary>멤버도 같은 키로 — "(table:1.2)"로 쓴 멤버는 table로 본다. 키가 같은 멤버가 둘이면(예: table과
    /// (table:1.2)) 하나로 합친다 — 안 그러면 태그 하나만 있어도 "2개 이상"으로 세어 오탐한다.
    /// 편 나눔에서 같은 키가 두 편에 있으면 그 태그 하나만으로 "두 편이 다 있음"이 돼 항상 충돌하므로
    /// 잘못된 규칙이다 — 조용히 넘기지 않고 예외(파일 로드는 <see cref="LoadFromFile"/>이 미리 걸러 건너뛴다).</summary>
    private static KeyedGroup BuildKeyed(ConflictGroup g, int index)
    {
        var exceptKeys = (g.Except ?? Array.Empty<string>()).Select(TagKey.KeyOf).ToArray();
        Member[] members;
        switch (g.Kind)
        {
            case ConflictKind.Sides:
            {
                var list = new List<Member>();
                var sideOf = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int s = 0; s < g.Sides!.Count; s++)
                    foreach (var name in g.Sides[s])
                    {
                        var key = TagKey.KeyOf(name);
                        if (sideOf.TryGetValue(key, out var prev))
                        {
                            if (prev != s)
                                throw new ArgumentException($"충돌 규칙 '{g.Label}': '{name}'이(가) 두 편에 동시에 있습니다.");
                            continue;
                        }
                        sideOf[key] = s;
                        list.Add(new Member(name, key, s, 1));
                    }
                members = list.ToArray();
                break;
            }
            case ConflictKind.Budget:
            {
                var costs = g.Budget!.Costs;
                if (costs.Count != g.Tags.Count)
                    throw new ArgumentException($"충돌 규칙 '{g.Label}': 비용 수({costs.Count})와 멤버 수({g.Tags.Count})가 다릅니다.");
                if (g.Budget.Limit < 0 || costs.Any(c => c < 0))
                    throw new ArgumentException($"충돌 규칙 '{g.Label}': 예산과 비용은 0 이상이어야 합니다.");
                members = g.Tags.Select((t, i) => new Member(t, TagKey.KeyOf(t), i, costs[i]))
                    .DistinctBy(m => m.Key).ToArray();
                break;
            }
            default:
                members = g.Tags.Select(t => (Name: t, Key: TagKey.KeyOf(t))).DistinctBy(m => m.Key)
                    .Select((m, i) => new Member(m.Name, m.Key, i, 1)).ToArray();
                break;
        }
        return new KeyedGroup { Index = index, Group = g, Members = members, ExceptKeys = exceptKeys };
    }

    /// <summary><paramref name="keys"/>에 멤버가 하나라도 있는 규칙들(원래 순서, 중복 없음).
    /// <para>왜: 규칙이 9천 개를 넘으면서 줄 하나(재추첨마다)를 검사할 때 전 규칙을 훑는 비용이 생성 시간의 94%를
    /// 차지했다(298팩 × 4옵션 × 60줄: 80초 중 75초, 2026-11-18 실측). 멤버가 하나도 없는 규칙은 절대 깨질 수 없다 —
    /// 상호배타·편 나눔은 멤버 둘 이상, 예산은 멤버 비용 합 &gt; 예산(≥0)이 필요하고, 면제·다인수 마커는 억제만 한다.
    /// 그래서 줄에 나온 키의 역인덱스로 추린 규칙만 봐도 결과가 전수 순회와 정확히 같다.</para>
    /// 시간: O(줄 키 수 × 키당 규칙 수 + 후보 규칙 수 log 후보 규칙 수) — 규칙 총수와 무관하다.</summary>
    private List<KeyedGroup> GroupsTouching(IEnumerable<string> keys)
    {
        var result = new List<KeyedGroup>();
        HashSet<int>? seen = null;
        foreach (var key in keys)
        {
            if (!_byMemberKey.TryGetValue(key, out var refs)) continue;
            foreach (var r in refs)
            {
                seen ??= new HashSet<int>();
                if (seen.Add(r.Rule.Index)) result.Add(r.Rule);
            }
        }
        if (result.Count > 1) result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }

    public static ConflictRules Empty { get; } = new(Array.Empty<ConflictGroup>());
    public IReadOnlyList<ConflictGroup> Groups => _groups;
    public int Count => _groups.Count;

    /// <summary>판정 키 하나가 어떤 규칙의 멤버인가(면제 태그는 제외). 레시피 검증이 "이 자연어 문장은 규칙에
    /// 직접 올라 있어 엔진이 본다"를 판단할 때 쓴다.</summary>
    public bool IsMember(string key) => _byMemberKey.ContainsKey(key);

    /// <summary>편 나눔 구분자(깊이 0에서만). 앞뒤 공백까지 포함해야 <c>vs</c>가 들어간 태그·문장과 안 겹친다.</summary>
    private const string SidesSeparator = " vs ";

    /// <summary>예산 규칙 머리: 라벨 칸이 <c>@자원:예산[ 설명]</c>. 예산은 0 이상 정수.</summary>
    private static readonly Regex BudgetHeader = new(@"^@(?<res>[^\s:|]+)\s*:\s*(?<limit>\S+)(?:\s+(?<label>.*))?$", RegexOptions.Compiled);

    /// <summary>예산 멤버의 <c>=비용</c> 꼬리. 소수까지 잡아 두고 정수가 아니면 줄 전체를 버린다
    /// (weight에 소수를 넣었다가 레시피가 조용히 빠진 사고와 같은 종류의 실수를 숫자 하나로 넘기지 않으려고).</summary>
    private static readonly Regex CostTail = new(@"=\s*(?<cost>[0-9]+(?:\.[0-9]*)?)\s*$", RegexOptions.Compiled);

    /// <summary>conflicts.csv 로드. 기본 줄 형식: "라벨|tag1,tag2,...[|scope][|except1,except2]".
    /// scope: character/캐릭터 → 캐릭터 단위(그 외/생략 → 장면 단위).
    /// except: 있으면 그 그룹을 면제하는 태그들. '#' 주석/빈 줄 무시.
    /// <para>확장 문법(기존 줄과 구분자로 식별 — 기존 줄은 한 글자도 안 바꿔도 예전과 똑같이 동작한다):
    /// <list type="bullet">
    /// <item>편 나눔: 태그 칸에 깊이 0의 <c> vs </c>가 있으면 <c>a,b vs c,d[ vs e]</c> — 서로 다른 편끼리만 충돌.
    /// 교차곱을 줄마다 펼쳐 쓴 "표정 상충" 같은 묶음을 한 줄로 쓴다. 한 태그가 두 편에 있으면 그 줄은 버린다.</item>
    /// <item>자원 예산: 라벨 칸이 <c>@자원:예산[ 설명]</c>이면 태그 칸은 <c>tag=비용,…</c>(비용 생략 시 1) —
    /// 같이 있는 멤버 비용 합이 예산을 넘으면 충돌. 예산·비용이 0 이상 정수가 아니면 그 줄은 버린다.</item>
    /// <item><c>requires|…</c> 줄은 상대 필요 규칙(<see cref="RequiresRules"/>) 문법이라 여기서는 건너뛴다.</item>
    /// </list></para></summary>
    public static ConflictRules LoadFromFile(string path)
    {
        if (!File.Exists(path)) return Empty;
        var groups = new List<ConflictGroup>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
            if (ParseLine(raw) is { } g) groups.Add(g);
        return new ConflictRules(groups);
    }

    /// <summary>한 줄 파싱. 규칙이 아니거나(주석·빈 줄·requires) 잘못된 줄이면 null.</summary>
    internal static ConflictGroup? ParseLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) return null;
        if (line.StartsWith(RequiresRules.LinePrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var parts = line.Split('|');
        string label = parts.Length >= 2 ? parts[0].Trim() : "충돌";
        string tagsField = parts.Length >= 2 ? parts[1] : parts[0];
        var scope = parts.Length >= 3 ? ParseScope(parts[2]) : ConflictScope.Scene;
        // 괄호 안 쉼표는 보존한다 — 자연어 멤버("(his hand gripping her jaw, prying…:1.2)")가
        // 쉼표에서 두 동강 나면 그 규칙은 영영 안 걸린다.
        var except = parts.Length >= 4 ? TagKey.SplitList(parts[3]) : Array.Empty<string>();

        if (label.StartsWith('@'))
        {
            var m = BudgetHeader.Match(label);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups["limit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int limit)) return null;
            var members = new List<(string, int)>();
            foreach (var piece in TagKey.SplitList(tagsField))
            {
                var (name, cost) = SplitCost(piece);
                if (cost < 0) return null;   // 정수가 아닌 비용
                members.Add((name, cost));
            }
            if (members.Count == 0) return null;
            var res = m.Groups["res"].Value;
            var text = m.Groups["label"].Success ? m.Groups["label"].Value.Trim() : "";
            return ConflictGroup.WithBudget(text.Length > 0 ? text : $"{res} 예산({limit}) 초과", res, limit, members, scope, except);
        }

        // 줄 끝이 " vs"면 줄 다듬기(Trim)에 구분자 뒤 공백이 먹힌 빈 편이다 — 상호배타로 잘못 읽지 않고 버린다.
        var trimmedField = tagsField.Trim();
        if (trimmedField.EndsWith(SidesSeparator.TrimEnd(), StringComparison.Ordinal)
            || trimmedField.StartsWith(SidesSeparator.TrimStart(), StringComparison.Ordinal)) return null;

        var cuts = TagKey.IndexesOfTopLevel(tagsField, SidesSeparator);
        if (cuts is { Count: > 0 })
        {
            var sides = new List<IReadOnlyList<string>>();
            int start = 0;
            foreach (var cut in cuts.Append(tagsField.Length))
            {
                var side = TagKey.SplitList(tagsField[start..cut]);
                if (side.Count == 0) return null;
                sides.Add(side);
                start = cut + SidesSeparator.Length;
            }
            // 같은 태그가 두 편에 있으면 그 태그 하나로 항상 충돌한다 → 잘못된 줄
            var sideOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int s = 0; s < sides.Count; s++)
                foreach (var key in sides[s].Select(TagKey.KeyOf))
                    if (sideOf.TryGetValue(key, out var prev) && prev != s) return null;
                    else sideOf[key] = s;
            return ConflictGroup.Bipartite(label, sides, scope, except);
        }

        var tags = TagKey.SplitList(tagsField);
        if (tags.Count < 2) return null;
        return new ConflictGroup(label, tags, scope, except);
    }

    /// <summary>"name=2" → (name, 2), "name" → (name, 1). 비용이 정수가 아니면 cost=-1.
    /// <c>=_=</c>처럼 이름 자체에 '='가 있는 태그는 숫자 꼬리가 없으므로 이름 그대로 둔다.</summary>
    private static (string Name, int Cost) SplitCost(string piece)
    {
        var m = CostTail.Match(piece);
        if (!m.Success) return (piece.Trim(), 1);
        var name = piece[..m.Index].Trim();
        if (name.Length == 0) return (piece.Trim(), 1);
        return int.TryParse(m.Groups["cost"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int c)
            ? (name, c) : (name, -1);
    }

    private static ConflictScope ParseScope(string token) =>
        token.Trim().ToLowerInvariant() is "character" or "char" or "캐릭터"
            ? ConflictScope.Character
            : ConflictScope.Scene;

    /// <summary>한 줄(태그 문자열들)에서 규칙이 깨지면 충돌로 검출(억제 그룹 제외).
    /// 각 문자열은 <see cref="TagKey"/>로 분해한 판정 키로 비교한다.</summary>
    public IReadOnlyList<ConflictHit> Detect(IEnumerable<string> tags)
    {
        var set = TagKey.KeySet(tags);
        var keys = new KeyView(set);
        var hits = new List<ConflictHit>();
        foreach (var g in GroupsTouching(set))
        {
            if (g.IsSuppressed(keys) || !g.Violated(keys)) continue;
            hits.Add(new ConflictHit(g.Group.Label, g.PresentIn(keys)));
        }
        return hits;
    }

    /// <summary>재추첨 루프의 판정이라 가장 자주 불린다. 결과가 참/거짓뿐이라 <see cref="GroupsTouching"/>의
    /// 정렬·중복 제거도 생략한다 — 멤버가 여럿 나온 규칙은 몇 번 다시 보지만, 그 비용이 집합 할당보다 싸다.</summary>
    public bool HasConflict(IEnumerable<string> tags)
    {
        var set = TagKey.KeySet(tags);
        var keys = new KeyView(set);
        foreach (var key in set)
        {
            if (!_byMemberKey.TryGetValue(key, out var refs)) continue;
            foreach (var r in refs)
                if (!r.Rule.IsSuppressed(keys) && r.Rule.Violated(keys)) return true;
        }
        return false;
    }

    /// <summary>슬롯 단위 로컬 필터용: 이미 확정된 줄 앞부분 <paramref name="current"/>에 후보 키
    /// <paramref name="added"/>를 더하면 <b>새로</b> 깨지는 규칙이 있는가.
    /// <list type="bullet">
    /// <item>후보 키를 멤버로 가진 규칙만 본다 — 면제·다인수 마커는 억제만 늘리므로 새 충돌을 못 만든다.</item>
    /// <item><paramref name="current"/>만으로 이미 깨진 규칙은 "새 충돌"이 아니다 — 고정 슬롯끼리의 항상 충돌
    /// 때문에 그 규칙을 건드리는 후보가 전부 걸러지면 다양성만 잃고 얻는 게 없다.</item>
    /// <item>억제는 합친 집합 기준(후보 자신이 면제 태그를 가져오면 통과). 뒤쪽 슬롯이 나중에 가져올
    /// 면제 태그는 모른다 — 앞에서부터 확정하는 방식의 한계라, 그 경우는 조금 엄격하게 걸러진다.</item>
    /// </list>
    /// <para>판정의 기준 구현이다. 생성기는 같은 판정을 증분으로 하는 <see cref="LineTracker"/>를 쓴다(결과 동일,
    /// 테스트가 둘을 대조한다).</para></summary>
    internal bool Introduces(HashSet<string> current, HashSet<string> added)
    {
        HashSet<KeyedGroup>? checkedGroups = null;
        var union = new KeyView(current, added);
        var before = new KeyView(current);
        foreach (var key in added)
        {
            if (!_byMemberKey.TryGetValue(key, out var refs)) continue;
            foreach (var r in refs)
            {
                var g = r.Rule;
                // 한 키의 목록 안에는 같은 규칙이 두 번 없다(멤버는 키로 중복 제거) — 후보 키가 여럿일 때만
                // 같은 규칙을 두 번 볼 수 있다.
                if (added.Count > 1)
                {
                    checkedGroups ??= new HashSet<KeyedGroup>(ReferenceEqualityComparer.Instance);
                    if (!checkedGroups.Add(g)) continue;
                }
                if (g.IsSuppressed(union) || !g.Violated(union)) continue;
                if (!g.IsSuppressed(before) && g.Violated(before)) continue;   // 원래 깨져 있던 규칙
                return true;
            }
        }
        return false;
    }

    /// <summary>후보(풀 후보 문자열 하나 또는 ALT 그룹 하나)가 건드리는 규칙들을 미리 정리해 둔 것.
    /// <see cref="LineTracker.Introduces"/>가 줄마다 쓴다 — 후보는 Generate 동안 안 바뀌므로 한 번만 만든다.</summary>
    internal sealed class Candidate
    {
        /// <summary>후보 키 중 멤버인 것들을 규칙별로 모은 것(규칙 중복 없음). Rule은 규칙 위치(<see cref="_keyed"/>),
        /// Members는 그 규칙에서 후보가 가진 멤버의 (키, 편, 비용), ExceptInCandidate는 후보가 그 규칙의 면제 키를 가졌는가.</summary>
        internal readonly (int Rule, (string Key, int Side, int Cost)[] Members, bool ExceptInCandidate)[] Touches;
        internal readonly bool HasMultiPersonMarker;

        internal Candidate(ConflictRules rules, HashSet<string> keys)
        {
            var byRule = new Dictionary<int, List<(string, int, int)>>();
            foreach (var key in keys)
            {
                if (!rules._byMemberKey.TryGetValue(key, out var refs)) continue;
                foreach (var r in refs)
                {
                    if (!byRule.TryGetValue(r.Rule.Index, out var members))
                        byRule[r.Rule.Index] = members = new List<(string, int, int)>();
                    members.Add((r.Key, r.Side, r.Cost));
                }
            }
            Touches = byRule
                .Select(e => (e.Key, e.Value.ToArray(), rules._keyed[e.Key].ExceptKeys.Any(keys.Contains)))
                .ToArray();
            HasMultiPersonMarker = keys.Overlaps(MultiPersonKeys);
        }
    }

    internal Candidate Prepare(HashSet<string> candidateKeys) => new(this, candidateKeys);

    internal LineTracker NewLineTracker() => new(this);

    /// <summary><see cref="Introduces"/>의 증분판: 줄에 확정된 키들이 각 규칙을 어디까지 채웠는지(편·예산 합·면제)를
    /// 키가 들어올 때마다 갱신해 두고, 후보 판정은 그 상태에 후보의 멤버만 얹어 본다.
    /// <para>왜: 로컬 필터는 줄마다 ALT 그룹·풀 후보 수백 개를 판정한다. 예전 판정은 후보가 건드리는 규칙마다 멤버
    /// 전체를 줄 집합과 대조해서, 단계 표식(implies:)처럼 큰 규칙 여럿에 걸친 키가 있는 레시피는 줄 하나에 멤버
    /// 대조가 15만 번을 넘었다(상납 · 구도형: 200줄 1.3초, 2026-11-20 실측). 증분 상태로는 규칙 하나 판정이
    /// "후보 안의 그 규칙 멤버 수"(보통 1)에 비례한다.</para>
    /// <para>풀 슬롯은 한 슬롯에서 여러 개를 뽑는 동안 "줄 + 이 슬롯에서 이미 뽑은 것" 기준으로 판정하고 끝나면
    /// 줄 기준으로 되돌린다 — <see cref="Mark"/>/<see cref="Rollback"/>.</para>
    /// <para>Generate 호출 하나(=스레드 하나) 안에서만 쓴다. 공유하지 말 것.</para></summary>
    internal sealed class LineTracker
    {
        private struct RuleState
        {
            public int Stamp;        // 이 값이 _stamp와 다르면 이번 줄에선 아직 손대지 않은 규칙(=빈 상태)
            public int FirstSide;    // 줄에 처음 나온 멤버의 편(-1: 없음)
            public bool MultiSide;   // 서로 다른 편이 둘 이상 나왔다(상호배타·편 나눔의 위반)
            public long Sum;         // 예산 규칙: 나온 멤버 비용 합
            public bool ExceptHit;   // 면제 키가 줄에 있다
        }

        private readonly ConflictRules _rules;
        private readonly RuleState[] _state;
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly List<string> _keyLog = new();
        private readonly List<(int Index, RuleState Old)> _ruleLog = new();
        private int _stamp = 1;
        private int _markers;

        internal LineTracker(ConflictRules rules)
        {
            _rules = rules;
            _state = new RuleState[rules._keyed.Count];
        }

        /// <summary>새 줄(재추첨 포함)을 시작한다.</summary>
        public void BeginLine()
        {
            if (_stamp == int.MaxValue) { Array.Clear(_state); _stamp = 0; }
            _stamp++;
            _keys.Clear();
            _keyLog.Clear();
            _ruleLog.Clear();
            _markers = 0;
        }

        private RuleState Get(int i) =>
            _state[i].Stamp == _stamp ? _state[i] : new RuleState { Stamp = _stamp, FirstSide = -1 };

        private void Set(int i, RuleState s)
        {
            _ruleLog.Add((i, _state[i]));
            _state[i] = s;
        }

        /// <summary>줄에 판정 키 하나를 확정한다(이미 있으면 아무 일도 안 한다).</summary>
        public void Add(string key)
        {
            if (!_keys.Add(key)) return;
            _keyLog.Add(key);
            if (MultiPersonKeys.Contains(key)) _markers++;

            if (_rules._byMemberKey.TryGetValue(key, out var refs))
                foreach (var r in refs)
                {
                    int i = r.Rule.Index;
                    var s = Get(i);
                    if (r.Rule.Kind == ConflictKind.Budget) s.Sum += r.Cost;
                    else if (s.FirstSide < 0) s.FirstSide = r.Side;
                    else if (r.Side != s.FirstSide) s.MultiSide = true;
                    Set(i, s);
                }

            if (_rules._byExceptKey.TryGetValue(key, out var exempted))
                foreach (var g in exempted)
                {
                    var s = Get(g.Index);
                    s.ExceptHit = true;
                    Set(g.Index, s);
                }
        }

        public void AddRange(IEnumerable<string> keys)
        {
            foreach (var k in keys) Add(k);
        }

        public (int Keys, int Rules) Mark() => (_keyLog.Count, _ruleLog.Count);

        /// <summary><see cref="Mark"/> 이후에 더한 키를 전부 되돌린다.</summary>
        public void Rollback((int Keys, int Rules) mark)
        {
            for (int n = _ruleLog.Count - 1; n >= mark.Rules; n--)
                _state[_ruleLog[n].Index] = _ruleLog[n].Old;
            _ruleLog.RemoveRange(mark.Rules, _ruleLog.Count - mark.Rules);

            for (int n = _keyLog.Count - 1; n >= mark.Keys; n--)
            {
                _keys.Remove(_keyLog[n]);
                if (MultiPersonKeys.Contains(_keyLog[n])) _markers--;
            }
            _keyLog.RemoveRange(mark.Keys, _keyLog.Count - mark.Keys);
        }

        /// <summary>지금 줄에 후보를 더하면 <b>새로</b> 깨지는 규칙이 있는가 — <see cref="Introduces"/>와 같은 판정.</summary>
        public bool Introduces(Candidate c)
        {
            foreach (var (rule, members, exceptInCandidate) in c.Touches)
            {
                var g = _rules._keyed[rule];
                var s = Get(rule);
                bool character = g.Group.Scope == ConflictScope.Character;
                bool suppressedBefore = (character && _markers > 0) || s.ExceptHit;
                // 합친 집합에서 억제되면 새 충돌이 아니다(후보가 면제 태그·다인수 마커를 가져오는 경우 포함).
                if (suppressedBefore || exceptInCandidate || (character && c.HasMultiPersonMarker)) continue;

                bool violatedAfter, violatedBefore;
                if (g.Kind == ConflictKind.Budget)
                {
                    long sum = s.Sum;
                    foreach (var m in members) if (!_keys.Contains(m.Key)) sum += m.Cost;   // 이미 줄에 있는 멤버는 한 번만
                    violatedAfter = sum > g.Limit;
                    violatedBefore = s.Sum > g.Limit;
                }
                else
                {
                    int first = s.FirstSide;
                    bool multi = s.MultiSide;
                    foreach (var m in members)
                    {
                        if (first < 0) first = m.Side;
                        else if (m.Side != first) multi = true;
                    }
                    violatedAfter = multi;
                    violatedBefore = s.MultiSide;
                }

                // 원래(억제 안 된 채) 깨져 있던 규칙은 "새 충돌"이 아니다.
                if (violatedAfter && !violatedBefore) return true;
            }
            return false;
        }
    }

    /// <summary>레시피 사전 점검. always는 항상 함께 나오는 태그(모든 고정 슬롯),
    /// reachable은 풀 후보까지 포함해 나올 수 있는 모든 태그.
    /// always에서 깨지면 Definite(항상 충돌), reachable에서 깨지면 Possible.</summary>
    public IReadOnlyList<RecipeConflict> AnalyzeRecipe(IEnumerable<string> alwaysTags, IEnumerable<string> reachableTags)
    {
        var alwaysSet = TagKey.KeySet(alwaysTags);
        var reachableSet = TagKey.KeySet(reachableTags);
        var always = new KeyView(alwaysSet);
        var reachable = new KeyView(reachableSet);
        var result = new List<RecipeConflict>();
        foreach (var g in GroupsTouching(alwaysSet.Concat(reachableSet)))
        {
            if (g.Violated(always))
            {
                result.Add(new RecipeConflict(g.Group.Label, g.PresentIn(always), ConflictSeverity.Definite));
                continue;
            }
            if (g.Violated(reachable))
                result.Add(new RecipeConflict(g.Group.Label, g.PresentIn(reachable), ConflictSeverity.Possible));
        }
        return result;
    }

    /// <summary>슬롯 하나를 분석용으로 정리한 것. Set: 슬롯 전체 키, Candidates: 후보 문자열별 키
    /// (랜덤 풀만 — ALT 그룹은 그 SlotSpec 자체가 후보 하나다).</summary>
    private sealed record RandomSlotInfo(HashSet<string> Set, bool Multi, int MaxCount, int? AltGroupOwnerId, List<HashSet<string>> Candidates)
    {
        /// <summary>키가 2개 이상인 후보(묶음 문자열)만. 상호배타 판정의 "후보 하나 안에서 공존" 검사는 이것만 보면 된다 —
        /// 단일 태그 후보가 대부분이라 규칙마다 전 후보를 훑던 비용이 크게 준다.</summary>
        public List<HashSet<string>> MultiKeyCandidates { get; } = Candidates.Where(c => c.Count >= 2).ToList();
    }

    /// <summary>슬롯 단위 사전 점검(권장). 같은 랜덤 슬롯에서 나오는 태그들은 그 슬롯의 MaxCount가
    /// 2 이상일 때만 서로 공존할 수 있다 — MaxCount=1이면 하나만 뽑히므로 그 풀 안의 상호배타
    /// 태그(예: 한 '시선' 풀 안의 looking_up/looking_down)는 충돌로 보지 않는다. 두 태그가
    ///   • 고정끼리 2개 이상        → Definite(항상 충돌)
    ///   • 서로 다른 출처(고정↔슬롯, 슬롯↔슬롯) 또는 같은 슬롯의 MaxCount≥2 → Possible(가끔)
    /// 억제(캐릭터 범위 다인수 / except)는 고정 태그를 기준으로 판정한다.
    /// <para>편 나눔 규칙은 교차 쌍(편 i의 a, 편 j의 b)마다 2인 상호배타로 펼쳐 같은 판정을 돌린 뒤 규칙 단위로
    /// 합친다 — "서로 다른 편이 같이 나옴" ⇔ "어떤 교차 쌍이 같이 나옴"이라 정확히 같은 결과다. 예산 규칙은
    /// 쌍으로 못 펼치므로(셋이 모여야 넘는 경우) <see cref="AnalyzeBudget"/>이 출처 조합을 직접 따진다.</para>
    /// <para>추첨 가중치 0(<c>0::tag</c>)인 랜덤 풀 후보는 절대 안 뽑히므로 빼고 본다(ALT의 weight 0 그룹과 같은 취급).</para></summary>
    public IReadOnlyList<RecipeConflict> AnalyzeRecipe(IReadOnlyList<SlotSpec> slots)
    {
        var fixedTags = TagKey.KeySet(slots.Where(s => s.IsFixed).SelectMany(s => s.Tags));
        // Candidates: 랜덤 풀 슬롯의 후보 문자열별 키 집합. 묶음 문자열("(문장:1.1), a, b")은 뽑히면
        // 조각이 전부 함께 나가므로, MaxCount=1이어도 그 후보 하나 안의 멤버끼리는 공존한다.
        var randomSlots = slots.Where(s => !s.IsFixed)
            .Select(s =>
            {
                var tags = s.AltGroupOwnerId == null
                    ? s.Tags.Where(t => TagKey.ParsePickWeight(t).Weight != 0).ToList()
                    : s.Tags.ToList();
                return new RandomSlotInfo(TagKey.KeySet(tags), s.MaxCount >= 2, s.MaxCount, s.AltGroupOwnerId,
                    s.AltGroupOwnerId == null
                        ? tags.Select(t => TagKey.KeySet(new[] { t })).ToList()
                        : new List<HashSet<string>>());
            })
            .ToList();

        // 레시피 어디서든 나올 수 있는 키. 판정에 쓰이는 모든 집합(고정·슬롯·후보)이 이것의 부분집합이라,
        // 여기 없는 멤버는 어떤 판정에도 끼지 못한다 — 그런 멤버만 가진 규칙·쌍을 미리 건너뛰어도 결과는 같다.
        // 왜: 편 나눔 규칙은 교차 쌍마다 전 슬롯·후보를 훑어서, 레시피와 무관한 규칙까지 다 돌면
        // 라이브러리 전체 배지 계산이 UI 스레드에서 수 초가 걸렸다(283팩 × 921규칙, 2026-11-12 실측 2.5초).
        var reachable = new HashSet<string>(fixedTags, StringComparer.Ordinal);
        foreach (var rs in randomSlots) reachable.UnionWith(rs.Set);

        bool fixedMultiPerson = fixedTags.Overlaps(MultiPersonMarkers);
        var result = new List<RecipeConflict>();
        // 멤버가 하나도 reachable에 없는 규칙은 아래 첫 검사에서 어차피 건너뛰므로, 역인덱스로 미리 추려 순회한다.
        foreach (var g in GroupsTouching(reachable))
        {
            int reachableMembers = 0;
            foreach (var m in g.Members) if (reachable.Contains(m.Key)) reachableMembers++;
            // 예산 규칙은 멤버 하나로도 넘을 수 있어(비용 > 예산) 하나도 없을 때만 건너뛴다.
            if (reachableMembers < (g.Kind == ConflictKind.Budget ? 1 : 2)) continue;

            if (g.Group.Scope == ConflictScope.Character && fixedMultiPerson) continue;
            if (g.ExceptKeys.Length > 0 && g.ExceptKeys.Any(fixedTags.Contains)) continue;

            switch (g.Kind)
            {
                case ConflictKind.Exclusive:
                    if (AnalyzeExclusive(g.Group.Label, g.Members, g.ExceptKeys, fixedTags, randomSlots) is { } hit)
                        result.Add(hit);
                    break;

                case ConflictKind.Sides:
                {
                    // 교차 쌍마다 2인 상호배타로 판정 → 규칙 하나로 합친다(심각도는 최악, 관련 태그는 합집합).
                    var involved = new List<string>();
                    bool any = false, definite = false;
                    for (int i = 0; i < g.Members.Length; i++)
                        for (int j = i + 1; j < g.Members.Length; j++)
                        {
                            if (g.Members[i].Side == g.Members[j].Side) continue;
                            if (!reachable.Contains(g.Members[i].Key) || !reachable.Contains(g.Members[j].Key)) continue;
                            var pair = new[] { g.Members[i] with { Side = 0 }, g.Members[j] with { Side = 1 } };
                            if (AnalyzeExclusive(g.Group.Label, pair, g.ExceptKeys, fixedTags, randomSlots) is not { } h) continue;
                            any = true;
                            definite |= h.Severity == ConflictSeverity.Definite;
                            foreach (var t in h.Tags) if (!involved.Contains(t, StringComparer.OrdinalIgnoreCase)) involved.Add(t);
                        }
                    if (any)
                        result.Add(new RecipeConflict(g.Group.Label, involved,
                            definite ? ConflictSeverity.Definite : ConflictSeverity.Possible));
                    break;
                }

                case ConflictKind.Budget:
                    if (AnalyzeBudget(g, fixedTags, randomSlots) is { } b) result.Add(b);
                    break;
            }
        }
        return result;
    }

    /// <summary>상호배타(멤버 2개 이상 공존 = 충돌) 한 그룹의 슬롯 단위 판정. 1-1 때의 판정을 그대로 옮긴 것.</summary>
    private static RecipeConflict? AnalyzeExclusive(
        string label, Member[] members, string[] exceptKeys, HashSet<string> fixedTags, List<RandomSlotInfo> randomSlots)
    {
        List<string> PresentIn(HashSet<string> keys)
        {
            var present = new List<string>();
            foreach (var m in members) if (keys.Contains(m.Key)) present.Add(m.Name);
            return present;
        }

        var inFixed = PresentIn(fixedTags);
        if (inFixed.Count >= 2)
            return new RecipeConflict(label, inFixed, ConflictSeverity.Definite);

        // 각 랜덤 슬롯에 들어 있는 이 그룹의 멤버
        var perSlot = randomSlots.Select(rs => PresentIn(rs.Set)).ToList();
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

        // 랜덤 풀 후보 문자열 하나(묶음) 안에 멤버 2개 이상 → 그 후보가 뽑히면 항상 같이 나온다.
        // 슬롯 자체는 가끔만 그 후보를 뽑으므로 Possible.
        foreach (var rs in randomSlots)
            foreach (var cand in rs.MultiKeyCandidates)
            {
                if (exceptKeys.Any(cand.Contains)) continue;
                var inCand = PresentIn(cand);
                if (inCand.Count < 2) continue;
                possible = true;
                foreach (var t in inCand) involved.Add(t);
            }

        if (definite && involved.Count >= 2)
            return new RecipeConflict(label, involved.ToList(), ConflictSeverity.Definite);
        if (possible && involved.Count >= 2)
            return new RecipeConflict(label, involved.ToList(), ConflictSeverity.Possible);
        return null;
    }

    /// <summary>조합 열거 상한. 넘으면 출처별 최대 비용의 합(상한 추정)으로 보수적으로 판정한다.</summary>
    private const int MaxBudgetCombinations = 50_000;

    /// <summary>출처 하나가 한 줄에 내놓을 수 있는 선택지 하나: 그 선택으로 나오는 멤버 키들과, 그 선택이
    /// 면제 태그를 같이 가져오는지(그러면 그 줄에서 규칙이 억제된다).</summary>
    private readonly record struct BudgetOption(HashSet<string> Members, bool Exempt);

    /// <summary>예산 규칙의 슬롯 단위 판정. 셋 이상이 모여야 예산을 넘는 경우가 있어 쌍으로 못 펼치므로,
    /// 출처(ALT 슬롯 하나 = 그룹 중 하나, 랜덤 풀 = 멤버 든 후보 MaxCount개까지의 조합)마다 선택지를 만들어
    /// 조합을 직접 따진다. 상호배타 판정과 같은 기준으로:
    /// <list type="bullet">
    /// <item>고정만으로 넘음, 또는 ALT 그룹 하나만으로 넘음 → Definite</item>
    /// <item>그 밖에 출처 조합(고정 포함)으로 넘을 수 있음 → Possible</item>
    /// </list>
    /// 같은 키는 한 번만 센다(런타임도 줄의 키 집합으로 판정). 랜덤 풀은 MinCount를 모르므로 "안 뽑음"도
    /// 선택지에 넣는다(보수적 — 놓치지 않는 쪽).</summary>
    private static RecipeConflict? AnalyzeBudget(KeyedGroup g, HashSet<string> fixedTags, List<RandomSlotInfo> randomSlots)
    {
        var memberKeys = g.Members.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var costOf = g.Members.ToDictionary(m => m.Key, m => m.Cost, StringComparer.Ordinal);
        var nameOf = g.Members.ToDictionary(m => m.Key, m => m.Name, StringComparer.Ordinal);
        long Cost(IEnumerable<string> keys) => keys.Distinct(StringComparer.Ordinal).Sum(k => (long)costOf[k]);
        List<string> Names(IEnumerable<string> keys) =>
            g.Members.Where(m => keys.Contains(m.Key, StringComparer.Ordinal)).Select(m => m.Name).ToList();

        var fixedMembers = memberKeys.Where(fixedTags.Contains).ToHashSet(StringComparer.Ordinal);
        if (Cost(fixedMembers) > g.Limit)
            return new RecipeConflict(g.Group.Label, Names(fixedMembers), ConflictSeverity.Definite);

        BudgetOption OptionOf(HashSet<string> keys) =>
            new(keys.Where(memberKeys.Contains).ToHashSet(StringComparer.Ordinal), g.ExceptKeys.Any(keys.Contains));

        // 출처별 선택지
        var sources = new List<List<BudgetOption>>();
        var definiteKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in randomSlots.Where(r => r.AltGroupOwnerId != null).GroupBy(r => r.AltGroupOwnerId))
        {
            var options = owner.Select(r => OptionOf(r.Set)).ToList();
            foreach (var o in options)
                if (!o.Exempt && Cost(o.Members) > g.Limit) definiteKeys.UnionWith(o.Members);
            if (options.Any(o => o.Members.Count > 0)) sources.Add(Distinct(options));
        }
        if (definiteKeys.Count > 0)
            return new RecipeConflict(g.Group.Label, Names(definiteKeys), ConflictSeverity.Definite);

        foreach (var rs in randomSlots.Where(r => r.AltGroupOwnerId == null))
        {
            var bearing = rs.Candidates.Select(OptionOf).Where(o => o.Members.Count > 0).ToList();
            if (bearing.Count == 0) continue;
            var options = new List<BudgetOption> { new(new HashSet<string>(StringComparer.Ordinal), false) };
            int maxPick = Math.Max(1, rs.MaxCount);
            void Combine(int start, int depth, HashSet<string> acc, bool exempt)
            {
                for (int i = start; i < bearing.Count; i++)
                {
                    var next = new HashSet<string>(acc, StringComparer.Ordinal);
                    next.UnionWith(bearing[i].Members);
                    bool ex = exempt || bearing[i].Exempt;
                    options.Add(new BudgetOption(next, ex));
                    if (options.Count > MaxBudgetCombinations) return;
                    if (depth + 1 < maxPick) Combine(i + 1, depth + 1, next, ex);
                }
            }
            Combine(0, 0, new HashSet<string>(StringComparer.Ordinal), false);
            sources.Add(Distinct(options));
        }

        long product = 1;
        foreach (var s in sources) product = Math.Min(product * s.Count, MaxBudgetCombinations + 1L);
        var involved = new HashSet<string>(StringComparer.Ordinal);

        if (product > MaxBudgetCombinations)
        {
            // 조합이 너무 많다 → 출처별 최대 비용 합으로 상한만 본다(놓치지 않는 쪽의 근사).
            long upper = Cost(fixedMembers) + sources.Sum(s => s.Where(o => !o.Exempt)
                .Select(o => Cost(o.Members.Where(k => !fixedMembers.Contains(k)))).DefaultIfEmpty(0).Max());
            if (upper <= g.Limit) return null;
            involved.UnionWith(fixedMembers);
            foreach (var s in sources) foreach (var o in s) involved.UnionWith(o.Members);
            return new RecipeConflict(g.Group.Label, Names(involved), ConflictSeverity.Possible);
        }

        var chosen = new HashSet<string>(fixedMembers, StringComparer.Ordinal);
        void Walk(int index, bool exempt)
        {
            if (index == sources.Count)
            {
                if (!exempt && Cost(chosen) > g.Limit) involved.UnionWith(chosen);
                return;
            }
            foreach (var o in sources[index])
            {
                var added = o.Members.Where(k => chosen.Add(k)).ToList();
                Walk(index + 1, exempt || o.Exempt);
                foreach (var k in added) chosen.Remove(k);
            }
        }
        Walk(0, false);

        return involved.Count > 0
            ? new RecipeConflict(g.Group.Label, Names(involved), ConflictSeverity.Possible)
            : null;
    }

    private static List<BudgetOption> Distinct(List<BudgetOption> options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BudgetOption>();
        foreach (var o in options)
            if (seen.Add((o.Exempt ? "1|" : "0|") + string.Join(",", o.Members.OrderBy(k => k, StringComparer.Ordinal))))
                result.Add(o);
        return result;
    }
}
