using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>프리셋을 현재 라이브러리에 맞춰 푼 결과.</summary>
/// <param name="MatchedRecipeIds">체크할 레시피 id들(중복 제거, 프리셋 순서 유지).</param>
/// <param name="MissingNames">현재 라이브러리에서 못 찾은 항목의 이름들.</param>
/// <param name="PruningWithheld">사라진 항목이 있는데도 안전장치가 발동해 정리를 보류했는지.
/// 참이면 호출부는 프리셋을 건드리지 말고 경고만 띄워야 한다.</param>
/// <param name="ShouldSave">프리셋 파일에 <paramref name="Entries"/>를 다시 써야 하는지.
/// 사라진 항목을 지웠거나, 이름 폴백으로 id를 갱신했을 때 참이 된다.</param>
/// <param name="Entries">프리셋에 저장할 엔트리. 보류된 경우 원본 그대로다.</param>
public sealed record BatchSelectionResolution(
    IReadOnlyList<string> MatchedRecipeIds,
    IReadOnlyList<string> MissingNames,
    bool PruningWithheld,
    bool ShouldSave,
    IReadOnlyList<BatchSelectionEntry> Entries);

/// <summary>저장해 둔 선택 목록을 현재 레시피 라이브러리에 맞춰 푼다.
/// <para>
/// 부수효과가 없는 순수 함수다 — 저장 여부는 호출부가 <see cref="BatchSelectionResolution.ShouldSave"/>를
/// 보고 결정한다. 뷰모델에 두면 단위 테스트를 못 하는데, "사라진 항목을 지운다"는 판단은
/// 잘못되면 사용자 프리셋이 통째로 날아가는 곳이라 반드시 테스트로 덮어야 한다.
/// </para></summary>
public static class BatchSelectionResolver
{
    /// <summary>매칭 실패가 이 비율 이상이면 정리를 보류한다. 라이브러리가 통째로 안 읽혔거나
    /// 번들 프리셋 갱신이 도는 중일 수 있고, 그 상태로 정리하면 프리셋이 사실상 사라진다.</summary>
    private const double PruneAbortRatio = 0.5;

    public static BatchSelectionResolution Resolve(BatchSelectionPreset preset, IReadOnlyList<Recipe> library)
    {
        var byId = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        var byName = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        foreach (var recipe in library)
        {
            byId[recipe.Id] = recipe;
            // 이름이 겹치면 먼저 나온 것을 쓴다 — 이름 폴백은 보조 수단이라 여기서 더 따지지 않는다.
            byName.TryAdd(recipe.Name, recipe);
        }

        var matched = new List<string>();
        var matchedSeen = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var kept = new List<BatchSelectionEntry>();
        bool idRefreshed = false;

        foreach (var entry in preset.Entries)
        {
            if (byId.TryGetValue(entry.RecipeId, out var hit))
            {
                if (matchedSeen.Add(hit.Id)) matched.Add(hit.Id);
                kept.Add(entry);
            }
            else if (!string.IsNullOrEmpty(entry.RecipeName) && byName.TryGetValue(entry.RecipeName, out var renamed))
            {
                if (matchedSeen.Add(renamed.Id)) matched.Add(renamed.Id);
                // id를 새 값으로 갱신해 다음 불러오기부터는 이름 폴백 없이 바로 잡히게 한다.
                // 이 갱신은 사라진 항목이 하나도 없을 때도 저장돼야 한다 — 안 그러면 매번
                // 이름 폴백을 다시 타고, 같은 이름의 다른 레시피가 생기면 엉뚱한 걸 잡는다.
                kept.Add(new BatchSelectionEntry { RecipeId = renamed.Id, RecipeName = renamed.Name });
                idRefreshed = true;
            }
            else
            {
                missing.Add(string.IsNullOrEmpty(entry.RecipeName) ? entry.RecipeId : entry.RecipeName);
            }
        }

        // 라이브러리 자체가 비정상으로 보이면(로드 실패, 번들 갱신 중) 아무것도 쓰지 않는다.
        // 이 상태에서 정리하면 프리셋이 사실상 사라지고, id를 갱신하면 엉뚱한 값이 박힌다.
        bool libraryLooksBroken =
            library.Count == 0 ||
            (missing.Count > 0 && missing.Count >= preset.Entries.Count * PruneAbortRatio);

        bool pruningWithheld = missing.Count > 0 && libraryLooksBroken;
        bool shouldSave = !libraryLooksBroken && (missing.Count > 0 || idRefreshed);

        return new BatchSelectionResolution(
            matched,
            missing,
            pruningWithheld,
            shouldSave,
            libraryLooksBroken ? preset.Entries : kept);
    }
}
