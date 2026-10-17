using System.Text;

namespace DanbooruTagGen.Core.Generation;

/// <summary>"이 태그가 나오면 저 태그들 중 하나가 같이 있어야 한다" 규칙 하나.
/// <see cref="StrictOnly"/>: 엄격 검사(tools/conflict_scan.py) 전용 — 앱 검증(<see cref="RecipeValidator"/>)은 건너뛴다.
/// 태그 이름은 파일 원문, 판정은 <see cref="TagKey.KeyOf"/> 키로 한다.</summary>
public sealed record RequiresRule(IReadOnlyList<string> Tags, IReadOnlyList<string> AnyOf, bool StrictOnly)
{
    internal HashSet<string> TagKeys { get; } = Tags.Select(TagKey.KeyOf).ToHashSet(StringComparer.Ordinal);
    internal HashSet<string> AnyOfKeys { get; } = AnyOf.Select(TagKey.KeyOf).ToHashSet(StringComparer.Ordinal);
}

/// <summary>data/requires.csv의 상대 필요 규칙. 예전엔 같은 표가 <see cref="RecipeValidator"/>(C#)와
/// <c>tools/conflict_scan.py</c>(NEEDS_PARTNER/PARTNER_MARKERS)에 따로 박혀 있어 한쪽만 고쳐지는 일이 생겼다.
/// 이제 둘 다 이 파일을 읽는다(파이썬은 load_requires).
/// <para>형식: <c>requires|태그1,태그2,…|any-of|필요1,필요2,…[|strict]</c>. 모드는 지금 <c>any-of</c>만 있다 —
/// 모르는 모드·빈 칸·한쪽만 있는 줄은 잘못된 줄로 보고 건너뛴다(conflicts.csv와 같은 관대한 로드).</para>
/// <para>실행 폴더의 data/requires.csv를 우선 읽고(<see cref="LoadFromFileOrDefault"/>), 없으면 빌드 때 Core에
/// 내장한 같은 파일(<see cref="Default"/>)을 쓴다. 내장본이 있어서 테스트·도구도 경로 없이 같은 표를 쓴다.</para></summary>
public sealed class RequiresRules
{
    /// <summary>이 문법의 줄 머리. <see cref="ConflictRules.LoadFromFile"/>은 이 머리의 줄을 건너뛴다.</summary>
    public const string LinePrefix = "requires|";

    private const string AnyOfMode = "any-of";
    private const string StrictFlag = "strict";
    private const string ResourceName = "DanbooruTagGen.Core.requires.csv";

    public IReadOnlyList<RequiresRule> Rules { get; }

    public RequiresRules(IEnumerable<RequiresRule> rules) => Rules = rules.ToList();

    public static RequiresRules Empty { get; } = new(Array.Empty<RequiresRule>());

    private static readonly Lazy<RequiresRules> _default = new(LoadEmbedded);

    /// <summary>빌드 때 내장한 data/requires.csv. 내장본이 없으면(리소스 누락 빌드) 빈 규칙.</summary>
    public static RequiresRules Default => _default.Value;

    public static RequiresRules Parse(IEnumerable<string> lines)
    {
        var rules = new List<RequiresRule>();
        foreach (var raw in lines)
            if (ParseLine(raw) is { } r) rules.Add(r);
        return new RequiresRules(rules);
    }

    public static RequiresRules LoadFromFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path, Encoding.UTF8)) : Empty;

    /// <summary>파일이 있으면 그것, 없으면 <see cref="Default"/>. 앱이 쓰는 경로 — 실행 폴더의 csv만 고쳐도
    /// 재빌드 없이 반영되게(pools.json·레시피처럼).</summary>
    public static RequiresRules LoadFromFileOrDefault(string path) =>
        File.Exists(path) ? LoadFromFile(path) : Default;

    internal static RequiresRule? ParseLine(string raw)
    {
        var line = raw.Trim();
        if (!line.StartsWith(LinePrefix, StringComparison.OrdinalIgnoreCase)) return null;   // 주석·빈 줄·다른 문법
        var parts = line.Split('|');
        if (parts.Length < 4) return null;
        if (!parts[2].Trim().Equals(AnyOfMode, StringComparison.OrdinalIgnoreCase)) return null;

        var tags = TagKey.SplitList(parts[1]);
        var anyOf = TagKey.SplitList(parts[3]);
        if (tags.Count == 0 || anyOf.Count == 0) return null;

        bool strict = false;
        if (parts.Length >= 5)
            foreach (var flag in parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (flag.Equals(StrictFlag, StringComparison.OrdinalIgnoreCase)) strict = true;
                else return null;   // 모르는 플래그는 오타일 가능성이 커서 줄째 버린다(조용히 엄격도가 바뀌지 않게)
            }
        return new RequiresRule(tags, anyOf, strict);
    }

    private static RequiresRules LoadEmbedded()
    {
        using var stream = typeof(RequiresRules).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null) return Empty;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } l) lines.Add(l);
        return Parse(lines);
    }
}
