using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>생성기가 태그 이름으로 메타데이터(빈도·그룹·카테고리)를 조회하는 통로.
/// 가중 추첨은 빈도를, 순서 자동 배치는 그룹/카테고리를 쓴다.
/// TagDatabase가 구현하며, 주입하지 않으면(=null) 두 기능 모두 비활성으로 폴백한다.
/// 왜 인터페이스인가: Core.Generation이 TagDatabase(검색/이진탐색 책임)에 직접
/// 의존하지 않도록 경계를 두어, 테스트에서 가짜 조회를 주입할 수 있게 한다.</summary>
public interface ITagLookup
{
    /// <summary>정확한 이름(언더스코어 원형)으로 태그를 찾는다. 없으면 null.</summary>
    Tag? Lookup(string tagName);
}
