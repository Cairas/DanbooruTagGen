using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>여러 개의 이름 붙은 레시피(슬롯 구성 전체)를 저장/불러오기. RecipeStore가 관리하는
/// "마지막 작업 중이던 레시피" 한 개짜리 자동저장과 달리, 이쪽은 PoolStore처럼 여러 프리셋을
/// 담는다 — "풀은 여러 개 저장해 놓고 골라 쓰는데 레시피 전체는 왜 하나뿐이냐"는 요청으로 추가.</summary>
public static class RecipeLibraryStore
{
    public static void Save(IEnumerable<Recipe> recipes, string path) => JsonStore.SaveAtomic(recipes.ToList(), path);

    public static List<Recipe> Load(string path)
    {
        if (Directory.Exists(path))
            return LoadDirectory(path);

        return JsonStore.LoadOrDefault(path, new List<Recipe>());
    }

    /// <summary>번들 레시피 폴더(data/presets/recipes) 전용 엄격 로드. 폴더가 없으면 빈 목록,
    /// 파일 하나라도 깨졌거나(JSON 오류·타입 불일치) 읽을 수 없으면 <see cref="InvalidDataException"/>.
    /// 깨진 파일을 건너뛰면 호출부가 "번들에서 삭제됨"으로 오인해 사용자 데이터에서 지우기 때문이다
    /// (<see cref="JsonStore.LoadStrict{T}"/> 참고).</summary>
    public static List<Recipe> LoadBundledDirectory(string dir) =>
        Directory.Exists(dir) ? LoadDirectory(dir, strict: true) : new List<Recipe>();

    private static List<Recipe> LoadDirectory(string path) => LoadDirectory(path, strict: false);

    private static List<Recipe> LoadDirectory(string path, bool strict)
    {
        var recipes = new List<Recipe>();
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var recipe = strict
                ? JsonStore.LoadStrict<Recipe?>(file, null)
                : JsonStore.LoadOrDefault<Recipe?>(file, null);
            if (recipe == null)
                continue;   // strict에선 파일이 그 사이 지워진 경우뿐(=정말로 번들에서 빠짐)

            if (string.IsNullOrWhiteSpace(recipe.Id))
                throw new InvalidDataException($"Recipe file '{file}' has no id.");

            if (seenIds.TryGetValue(recipe.Id, out var previousFile))
                throw new InvalidDataException(
                    $"Duplicate recipe id '{recipe.Id}' in '{previousFile}' and '{file}'.");

            seenIds.Add(recipe.Id, file);
            recipes.Add(recipe);
        }

        return recipes;
    }
}
