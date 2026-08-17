using System.Text;
using System.Text.Json;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

public static class RecipeStore
{
    public static void Save(Recipe recipe, string path)
    {
        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static Recipe Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("레시피 파일을 찾을 수 없습니다.", path);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)
            ?? throw new InvalidDataException("레시피 역직렬화 결과가 null입니다.");
    }
}
