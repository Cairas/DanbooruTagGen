namespace DanbooruTagGen.Core.Models;

/// <summary>공용 풀의 역참조("이 풀을 누가 쓰고 있나") 조회.
///
/// 왜 필요한가: 풀 하나를 여러 레시피가 Id로 공유하는 게 이 프로그램의 핵심 구조인데,
/// 정작 삭제·수정 시점에는 그 영향 범위가 화면에 전혀 안 보였다. 참조가 끊긴 레시피는
/// 다음 생성 때 "참조하는 풀을 찾을 수 없습니다"로 막히고, 풀 삭제에는 되돌리기가 없다.</summary>
public static class PoolReferences
{
    /// <summary>이 풀을 참조하는 레시피 이름들(중복 없이, 목록 순서대로).
    /// 슬롯의 <see cref="RandomPoolSlot.PoolId"/>와 체이닝된 <see cref="RandomPoolSlot.ExtraPoolIds"/>를
    /// 모두 본다 — 생성기가 후보를 합칠 때 쓰는 기준(ResolveCandidates)과 같다.</summary>
    public static IReadOnlyList<string> FindRecipesUsing(string poolId, IEnumerable<Recipe> recipes)
    {
        if (string.IsNullOrEmpty(poolId)) return Array.Empty<string>();
        return recipes
            .Where(r => r.Slots.OfType<RandomPoolSlot>()
                         .Any(s => s.PoolId == poolId || s.ExtraPoolIds.Contains(poolId)))
            .Select(r => r.Name)
            .ToList();
    }
}
