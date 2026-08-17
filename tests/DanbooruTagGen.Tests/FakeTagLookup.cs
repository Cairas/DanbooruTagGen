using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Tests;

/// <summary>테스트용 ITagLookup. 태그명 → (카테고리, 빈도, 별칭) 메타데이터를 주입한다.
/// 별칭에 17개 그룹 키워드를 넣어 순서 정렬을, 빈도로 가중 추첨을 결정적으로 검증한다.</summary>
internal sealed class FakeTagLookup : ITagLookup
{
    private readonly Dictionary<string, Tag> _map = new(StringComparer.Ordinal);

    public FakeTagLookup Add(string name, int postCount, params string[] aliases)
    {
        _map[name] = new Tag(name, TagCategory.General, postCount, aliases);
        return this;
    }

    public FakeTagLookup Add(string name, TagCategory category, int postCount, params string[] aliases)
    {
        _map[name] = new Tag(name, category, postCount, aliases);
        return this;
    }

    public Tag? Lookup(string tagName) => _map.TryGetValue(tagName, out var t) ? t : null;
}
