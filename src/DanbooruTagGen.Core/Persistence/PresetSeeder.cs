using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>프로그램 제공 프리셋(번들 풀·레시피, data/presets)을 사용자 데이터에 "한 번만" 주입한다.
///
/// 왜 매번 병합하지 않고 시딩인가: 매 실행 병합이면 사용자가 지운 번들 항목이 다음 실행 때
/// 부활한다. 대신 "한 번이라도 주입한 프리셋 id"를 seededIds(Settings에 영속)로 기억해,
/// 그 뒤의 삭제·수정은 전부 사용자 결정으로 존중한다. 업데이트로 번들에 새 프리셋이
/// 추가되면(=아직 seededIds에 없는 id) 다음 실행 때 자동으로 들어온다.
///
/// 결과적으로 깃/배포에는 번들 파일만 올라가고, 사용자 커스텀(자작 풀·레시피, 번들 항목의
/// 수정·삭제 상태)은 %APPDATA%에만 남는다.</summary>
public static class PresetSeeder
{
    /// <summary>번들 프리셋 중 아직 시딩 안 된 것을 사용자 목록에 추가한다.
    /// 반환값이 true면 호출자가 사용자 파일과 seededIds를 저장해야 한다.
    /// 이름이 같은 레시피/같은 id의 풀이 이미 있으면 추가하지 않고 시딩된 것으로만 기록한다
    /// (프리셋이 시딩 도입 전에 사용자 파일로 직접 설치됐던 과거 상태와의 호환).</summary>
    public static bool Seed(
        List<Pool> userPools, IReadOnlyList<Pool> bundledPools,
        List<Recipe> userRecipes, IReadOnlyList<Recipe> bundledRecipes,
        ISet<string> seededIds)
    {
        bool changed = false;

        foreach (var pool in bundledPools)
        {
            if (!seededIds.Add(pool.Id)) continue;      // 이미 시딩됨(삭제됐어도 존중)
            changed = true;
            if (userPools.All(u => u.Id != pool.Id))
                userPools.Add(pool);
        }

        // 이름이 같은 사본을 "이미 설치됨"으로 볼 때 번들 팩끼리의 동명이인은 빼야 한다.
        // 그러지 않으면 이름이 겹치는 두 번째 번들 팩이 첫 번째에 가려 추가되지 않는데도
        // 시딩됨으로 기록돼, 재시작해도 영영 안 들어오는 유령이 된다.
        var bundledRecipeIds = bundledRecipes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var recipe in bundledRecipes)
        {
            if (!seededIds.Add(recipe.Id)) continue;
            changed = true;
            bool alreadyInstalled =
                userRecipes.Any(u => u.Id == recipe.Id)
                || userRecipes.Any(u => u.Name == recipe.Name && !bundledRecipeIds.Contains(u.Id));
            if (!alreadyInstalled)
                userRecipes.Add(recipe);
        }

        return changed;
    }

    /// <summary>번들 프리셋의 최신 내용을 사용자 데이터에 덮어쓴다(사용자가 명시적으로 요청할 때만).
    ///
    /// Seed는 "한 번만 주입"이라 이미 시딩된 항목의 내용 변경(태그 보강·슬롯 추가 등)은 전달되지
    /// 않는다. 이 메서드가 그 갱신 경로다.
    ///   - 풀: 같은 Id만 교체한다. 이름으로 찾아 교체하면 Id가 바뀌어 그 풀을 참조하는 레시피의
    ///     poolId가 끊기므로 하지 않는다.
    ///   - 레시피: 같은 Id, 없으면 같은 Name을 교체한다(Id 도입 전에 설치된 구버전 사본 구제).
    ///     레시피는 다른 항목이 참조하지 않아 Id가 바뀌어도 안전하다.
    ///   - 사용자가 지운 번들 항목(Id/Name 둘 다 없음)은 되살리지 않는다.
    ///   - 번들에 없는 자작 항목은 손대지 않는다.
    ///   - seededIds에는 있지만(=한때 번들에 있었음) 지금 번들 목록엔 없는 항목(=제작자가 번들에서
    ///     완전히 삭제/통합함)은 사용자 데이터에서도 같이 제거한다. seededIds에 없는 항목(자작품,
    ///     또는 id 도입 전 레거시 사본)은 절대 건드리지 않는다.
    ///
    /// 주의: 사용자가 번들 항목을 직접 수정했다면 그 수정은 덮어써진다(갱신의 의도).
    /// <paramref name="allowRemoval"/>가 false면 교체·복구만 하고 제거는 건너뛴다(자동 경로의
    /// 대량 제거 보류용 — <see cref="CountPendingRemovals"/> 참고).
    /// 반환: (교체된 항목 수, 제거된 항목 수).</summary>
    public static (int Updated, int Removed) SyncBundled(
        List<Pool> userPools, IReadOnlyList<Pool> bundledPools,
        List<Recipe> userRecipes, IReadOnlyList<Recipe> bundledRecipes,
        ISet<string> seededIds, bool allowRemoval = true)
    {
        int updated = 0;

        foreach (var pool in bundledPools)
        {
            int i = userPools.FindIndex(u => u.Id == pool.Id);
            if (i < 0) continue;                       // 지웠거나 아직 미시딩 → Seed의 몫
            userPools[i] = pool;
            updated++;
        }

        foreach (var recipe in bundledRecipes)
        {
            int i = userRecipes.FindIndex(u => u.Id == recipe.Id);
            if (i < 0) i = userRecipes.FindIndex(u => u.Name == recipe.Name);
            if (i < 0) continue;
            userRecipes[i] = recipe;
            updated++;
        }

        // 참조가 끊긴 풀 복구.
        // 위 루프는 "사용자가 지운 건 되살리지 않는다"는 원칙 때문에 사용자 목록에 없는 풀을
        // 건너뛴다. 그런데 Seed는 이미 시딩된 id를 다시 넣지 않으므로, 한 번 사라진 번들 풀은
        // 영영 복구되지 않는다. 그 풀을 참조하는 레시피는 "참조하는 풀을 찾을 수 없습니다"로
        // 생성 자체가 막힌다(실제로 발생한 장애).
        // 그래서 **레시피가 실제로 참조하는 풀만** 되살린다 — 아무도 안 쓰는 풀은 사용자의
        // 삭제 의사를 그대로 존중하고, 깨진 참조만 치료한다.
        var referencedPoolIds = userRecipes
            .SelectMany(r => r.Slots)
            .OfType<RandomPoolSlot>()
            .SelectMany(s => s.ExtraPoolIds.Prepend(s.PoolId))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();
        foreach (var pool in bundledPools)
        {
            if (!referencedPoolIds.Contains(pool.Id)) continue;
            if (userPools.Any(u => u.Id == pool.Id)) continue;
            userPools.Add(pool);
            updated++;
        }

        if (!allowRemoval) return (updated, 0);

        var bundledPoolIds = bundledPools.Select(p => p.Id).ToHashSet();
        var bundledRecipeIds = bundledRecipes.Select(r => r.Id).ToHashSet();
        var removedIds = new List<string>();
        int removed = userPools.RemoveAll(u =>
        {
            if (!seededIds.Contains(u.Id) || bundledPoolIds.Contains(u.Id)) return false;
            removedIds.Add(u.Id);
            return true;
        });
        removed += userRecipes.RemoveAll(u =>
        {
            if (!seededIds.Contains(u.Id) || bundledRecipeIds.Contains(u.Id)) return false;
            removedIds.Add(u.Id);
            return true;
        });

        // 장부에서도 같이 지운다. 안 지우면 "목록엔 없고 장부엔 있음" = 사용자가 지운 것과
        // 구분이 안 돼서, 번들에 같은 id가 돌아와도 Seed가 영영 다시 넣지 않는다(유령 시딩).
        // 지워 두면 번들 복귀 시 "아직 시딩 안 된 새 항목"으로 자연스럽게 다시 들어온다.
        foreach (var id in removedIds) seededIds.Remove(id);

        return (updated, removed);
    }

    /// <summary><see cref="SyncBundled"/>가 지금 실행되면 제거할 항목 수(아무것도 바꾸지 않음).
    /// 자동 경로(워처·퀵 갱신)는 이 값이 임계값을 넘으면 제거를 보류한다 — 대량 제거는
    /// 번들 일부가 잠깐 안 보이는 순간(폴더 이동·체크아웃 중 등)일 가능성이 높아서다.</summary>
    public static int CountPendingRemovals(
        IReadOnlyList<Pool> userPools, IReadOnlyList<Pool> bundledPools,
        IReadOnlyList<Recipe> userRecipes, IReadOnlyList<Recipe> bundledRecipes,
        ISet<string> seededIds)
    {
        var bundledPoolIds = bundledPools.Select(p => p.Id).ToHashSet();
        var bundledRecipeIds = bundledRecipes.Select(r => r.Id).ToHashSet();
        return userPools.Count(u => seededIds.Contains(u.Id) && !bundledPoolIds.Contains(u.Id))
             + userRecipes.Count(u => seededIds.Contains(u.Id) && !bundledRecipeIds.Contains(u.Id));
    }

    /// <summary>Seed/SyncBundled 뒤 장부(seededIds)를 언제 무엇으로 저장할지 정한다.
    /// <para>추가와 제거는 크래시 안전 순서가 반대다.
    /// 추가된 id는 데이터 저장 <b>뒤에</b> 장부에 써야 한다(먼저 쓰고 죽으면 장부에만 남는 유령).
    /// 제거된 id는 데이터 저장 <b>전에</b> 장부에서 빼야 한다(데이터만 쓰고 죽으면 역시 장부에만
    /// 남는 유령). 그래서 제거가 있으면 "이전 장부 − 제거분"을 데이터보다 먼저, 추가가 있으면
    /// 최종 장부를 데이터 뒤에 쓴다. 어느 틈에 죽어도 다음 실행의 Seed가 같은 id 사본을
    /// "이미 설치됨"으로 흡수하므로 중복도 유령도 생기지 않는다.</para>
    /// 반환값의 각 목록이 null이면 그 시점엔 장부를 쓸 필요가 없다.</summary>
    public static (List<string>? BeforeData, List<string>? AfterData) PlanLedgerWrites(
        IReadOnlyList<string> previous, ISet<string> current)
    {
        var previousSet = previous.ToHashSet(StringComparer.Ordinal);
        bool anyRemoved = previousSet.Any(id => !current.Contains(id));
        bool anyAdded = current.Any(id => !previousSet.Contains(id));

        var before = anyRemoved ? previous.Where(current.Contains).Distinct(StringComparer.Ordinal).ToList() : null;
        var after = anyAdded ? current.ToList() : null;
        return (before, after);
    }
}
