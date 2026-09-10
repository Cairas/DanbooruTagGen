using System.Text.Json;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>번들 프리셋 한 벌(축 풀 + 컨셉 팩).</summary>
public sealed record BundledPresets(IReadOnlyList<Pool> Pools, IReadOnlyList<Recipe> Recipes)
{
    public static BundledPresets Empty { get; } = new(Array.Empty<Pool>(), Array.Empty<Recipe>());
}

/// <summary>data/presets를 읽되 <b>실패를 예외가 아니라 값으로</b> 돌려준다.
///
/// 왜 필요한가: 이 읽기는 앱 생성자와 FileSystemWatcher 타이머, 두 군데서 불린다. 둘 다
/// 예외를 받아 줄 곳이 없어서, 레시피 파일에 id가 겹치거나(작성 중인 새 팩) 파일이 잠겨 있는
/// 순간에 걸리면 앱이 그대로 죽었다 — 워처는 "쓰는 중"인 파일을 정확히 그 타이밍에 읽으러
/// 들어오므로 드문 일이 아니다.
///
/// 진단 자체는 버리지 않는다. id 중복 같은 제작 실수는 <see cref="RecipeLibraryStore"/>가
/// 계속 예외로 알려 주고(fail-fast), 이 로더가 그 메시지를 문자열로 바꿔 호출부가 상태
/// 표시줄에 띄울 수 있게 한다.</summary>
public static class BundledPresetLoader
{
    /// <summary>번들 풀·레시피를 읽는다. 읽을 게 하나도 없거나 읽다가 실패하면 false와
    /// 사람이 읽을 <paramref name="error"/>를 돌려준다(예외는 던지지 않는다).</summary>
    public static bool TryLoad(string poolsFile, string recipesDir, out BundledPresets presets, out string error)
    {
        presets = BundledPresets.Empty;
        error = "";
        try
        {
            var pools = PoolStore.Load(poolsFile);
            var recipes = RecipeLibraryStore.Load(recipesDir);
            if (pools.Count == 0 && recipes.Count == 0)
            {
                error = "번들 프리셋을 찾을 수 없습니다.";
                return false;
            }
            presets = new BundledPresets(pools, recipes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
