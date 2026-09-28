using System.Text;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Tags;

/// <summary>태그 사전. 이름/별칭 prefix 자동완성을 빈도순으로 제공한다.
/// 생성기에 빈도·그룹 메타데이터를 공급하기 위해 ITagLookup도 구현한다.</summary>
public sealed class TagDatabase : ITagLookup
{
    // (lowercased key, tag) 쌍을 key 정렬해 prefix 이진탐색에 사용.
    private readonly (string Key, Tag Tag)[] _byName;
    private readonly (string Key, Tag Tag)[] _byAlias;
    // 정확한 이름(원형, 대소문자 무시) → 태그. 생성기의 O(1) 메타데이터 조회용.
    private readonly Dictionary<string, Tag> _exactByName;

    private TagDatabase(Tag[] tags)
    {
        Count = tags.Length;
        _byName = tags
            .Select(t => (Key: Normalize(t.Name), Tag: t))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        _byAlias = tags
            .SelectMany(t => t.Aliases.Select(a => (Key: Normalize(a), Tag: t)))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        // 같은 이름이 중복되면 먼저 온 것을 유지(LoadFromFiles가 이미 이름 단위 병합하므로 보통 유일).
        _exactByName = new Dictionary<string, Tag>(tags.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tags) _exactByName[t.Name] = t;
    }

    /// <summary>정확한 이름으로 태그를 조회한다(ITagLookup). 없으면 null.</summary>
    public Tag? Lookup(string tagName) =>
        tagName is not null && _exactByName.TryGetValue(tagName, out var t) ? t : null;

    /// <summary>검색 정규화: 소문자 + '_'를 공백으로. 언더스코어/공백을 같게 취급해
    /// "long hair"로도 "long_hair"가 검색되게 한다.</summary>
    private static string Normalize(string s) => s.ToLowerInvariant().Replace('_', ' ');

    public int Count { get; }

    public static TagDatabase FromTags(IEnumerable<Tag> tags) => new(tags.ToArray());

    public static TagDatabase LoadFromFile(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("태그 csv를 찾을 수 없습니다.", csvPath);
        return LoadFromFiles(new[] { csvPath });
    }

    /// <summary>여러 csv를 순서대로 읽어 이름 기준 병합. 같은 태그가 여러 파일에 나오면
    /// 카테고리·빈도는 먼저 온 파일 것을 유지하고, 별칭은 합집합으로 누적한다(뒤 파일이
    /// 한국어 별칭 등을 보강). UTF-8(한/일/중 별칭 보존).</summary>
    public static TagDatabase LoadFromFiles(IEnumerable<string> csvPaths)
    {
        var byName = new Dictionary<string, Tag>(StringComparer.Ordinal);
        foreach (var path in csvPaths)
        {
            if (!File.Exists(path)) continue;
            // 파일명에 nsfw가 들어가면 성인 큐레이션 파일로 간주(ko-nsfw.csv).
            bool fileIsNsfw = Path.GetFileName(path).Contains("nsfw", StringComparison.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (!CsvTagParser.TryParseLine(line, out var tag)) continue;
                if (fileIsNsfw && !tag.IsNsfw) tag = tag with { IsNsfw = true };
                if (byName.TryGetValue(tag.Name, out var existing))
                {
                    // 카테고리/빈도는 첫 파일 유지, 별칭은 합집합, 설명은 먼저 채워진 것 유지, nsfw는 OR
                    var aliases = tag.Aliases.Count > 0 ? UnionAliases(existing.Aliases, tag.Aliases) : existing.Aliases;
                    var description = string.IsNullOrEmpty(existing.Description) ? tag.Description : existing.Description;
                    bool isNsfw = existing.IsNsfw || tag.IsNsfw;
                    if (!ReferenceEquals(aliases, existing.Aliases) || description != existing.Description || isNsfw != existing.IsNsfw)
                        byName[tag.Name] = existing with { Aliases = aliases, Description = description, IsNsfw = isNsfw };
                }
                else
                {
                    byName[tag.Name] = tag;
                }
            }
        }
        return new TagDatabase(byName.Values.ToArray());
    }

    /// <summary>순서를 보존하며 중복(대소문자 무시) 없이 별칭을 합친다.</summary>
    private static IReadOnlyList<string> UnionAliases(IReadOnlyList<string> first, IReadOnlyList<string> extra)
    {
        var seen = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(first);
        foreach (var a in extra)
            if (seen.Add(a)) result.Add(a);
        return result;
    }

    /// <summary>인기순(빈도 내림차순) 상위 태그. 검색어 없이 "전체" 둘러보기에 사용.
    /// filter를 주면 그 조건을 만족하는 태그만(예: 성인/일반).</summary>
    public IReadOnlyList<Tag> TopByPostCount(int limit = 300, Func<Tag, bool>? filter = null)
    {
        if (limit <= 0) return Array.Empty<Tag>();
        IEnumerable<Tag> q = _byName.Select(x => x.Tag);
        if (filter != null) q = q.Where(filter);
        return q.OrderByDescending(t => t.PostCount).Take(limit).ToList();
    }

    /// <summary>컨셉 빌더 위저드용 테마 검색: 이름·별칭뿐 아니라 **한국어 설명**까지 부분 일치로
    /// 뒤진다. "비", "온천" 같은 테마 단어는 태그명엔 없어도 설명 문장엔 자주 등장하므로,
    /// 설명 검색이 있어야 테마 관련 태그가 폭넓게 걸린다. 전체 선형 스캔이지만 14만 건
    /// 문자열 포함 검사 수준이라 위저드의 단발 호출에는 충분히 빠르다.</summary>
    public IReadOnlyList<Tag> SearchTheme(string keyword, int limit = 400)
    {
        if (string.IsNullOrWhiteSpace(keyword) || limit <= 0)
            return Array.Empty<Tag>();

        var key = Normalize(keyword);
        var hits = new List<Tag>();
        foreach (var (nameKey, tag) in _byName)
        {
            if (nameKey.Contains(key, StringComparison.Ordinal)
                || tag.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || tag.Aliases.Any(a => Normalize(a).Contains(key, StringComparison.Ordinal)))
                hits.Add(tag);
        }
        return hits.OrderByDescending(t => t.PostCount).Take(limit).ToList();
    }

    public IReadOnlyList<Tag> Autocomplete(string prefix, int limit = 20)
    {
        if (string.IsNullOrEmpty(prefix) || limit <= 0)
            return Array.Empty<Tag>();

        var key = Normalize(prefix);
        var seen = new HashSet<Tag>();
        var hits = new List<Tag>();

        CollectPrefix(_byName, key, seen, hits);
        CollectPrefix(_byAlias, key, seen, hits);

        return hits
            .OrderByDescending(t => t.PostCount)
            .Take(limit)
            .ToList();
    }

    /// <summary>사용자 자유 검색용. Autocomplete와 달리 정확일치가 아니어도, 접두어가
    /// 아니어도(부분 포함) 결과에 나온다 — 다만 등급을 매겨 정확일치 → 접두일치 → 부분포함
    /// 순으로, 각 등급 안에서는 빈도 내림차순으로 정렬한다.
    /// 부분포함은 정렬된 배열의 이진탐색을 못 써 전체 스캔이 필요하므로, 상위 등급만으로
    /// limit을 채울 수 있으면 건너뛴다(카테고리 브라우징처럼 큰 limit을 쓰는 호출에서
    /// 불필요한 풀스캔을 피함). 카테고리 버튼 브라우징은 이 메서드가 아니라 Autocomplete를
    /// 그대로 쓴다 — 부분포함까지 섞으면 그룹 키워드가 우연히 다른 별칭 안에 부분 문자열로
    /// 들어간 무관한 태그가 섞여 큐레이션 그룹의 정확성이 깨지기 때문.</summary>
    public IReadOnlyList<Tag> SearchRanked(string query, int limit = 20)
    {
        if (string.IsNullOrEmpty(query) || limit <= 0)
            return Array.Empty<Tag>();

        var key = Normalize(query);
        var seen = new HashSet<Tag>();
        var exact = new List<Tag>();
        var startsWith = new List<Tag>();

        CollectExactAndPrefix(_byName, key, seen, exact, startsWith);
        CollectExactAndPrefix(_byAlias, key, seen, exact, startsWith);

        var result = new List<Tag>(exact.Count + startsWith.Count);
        result.AddRange(exact.OrderByDescending(t => t.PostCount));
        result.AddRange(startsWith.OrderByDescending(t => t.PostCount));

        if (result.Count < limit)
        {
            var contains = new List<Tag>();
            CollectContains(_byName, key, seen, contains);
            CollectContains(_byAlias, key, seen, contains);
            result.AddRange(contains.OrderByDescending(t => t.PostCount));
        }

        return result.Take(limit).ToList();
    }

    private static void CollectPrefix((string Key, Tag Tag)[] sorted, string key,
        HashSet<Tag> seen, List<Tag> hits)
    {
        int lo = LowerBound(sorted, key);
        for (int i = lo; i < sorted.Length; i++)
        {
            if (!sorted[i].Key.StartsWith(key, StringComparison.Ordinal)) break;
            if (seen.Add(sorted[i].Tag)) hits.Add(sorted[i].Tag);
        }
    }

    /// <summary>정렬된 배열의 이진탐색으로 접두 구간을 찾아, key와 완전히 같으면 exact로,
    /// 그 외(접두이지만 더 긴)는 startsWith로 나눈다.</summary>
    private static void CollectExactAndPrefix((string Key, Tag Tag)[] sorted, string key,
        HashSet<Tag> seen, List<Tag> exact, List<Tag> startsWith)
    {
        int lo = LowerBound(sorted, key);
        for (int i = lo; i < sorted.Length; i++)
        {
            if (!sorted[i].Key.StartsWith(key, StringComparison.Ordinal)) break;
            if (!seen.Add(sorted[i].Tag)) continue;
            (sorted[i].Key == key ? exact : startsWith).Add(sorted[i].Tag);
        }
    }

    /// <summary>이진탐색 구간 밖(접두가 아닌 부분포함)을 찾기 위한 전체 선형 스캔.
    /// 상위 등급으로 limit을 못 채웠을 때만 호출되는 폴백이라 빈도가 낮다.</summary>
    private static void CollectContains((string Key, Tag Tag)[] sorted, string key,
        HashSet<Tag> seen, List<Tag> contains)
    {
        foreach (var (k, tag) in sorted)
        {
            if (seen.Contains(tag)) continue;
            if (k.Contains(key, StringComparison.Ordinal) && seen.Add(tag))
                contains.Add(tag);
        }
    }

    /// <summary>key 이상인 첫 인덱스(이진탐색).</summary>
    private static int LowerBound((string Key, Tag Tag)[] sorted, string key)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (string.CompareOrdinal(sorted[mid].Key, key) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
