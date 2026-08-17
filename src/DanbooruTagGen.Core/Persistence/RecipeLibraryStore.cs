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

    private static List<Recipe> LoadDirectory(string path)
    {
        var recipes = new List<Recipe>();
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var recipe = JsonStore.LoadOrDefault<Recipe?>(file, null);
            if (recipe == null)
                continue;

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
