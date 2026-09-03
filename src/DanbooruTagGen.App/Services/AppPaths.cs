using System.IO;

namespace DanbooruTagGen.App.Services;

/// <summary>앱이 쓰는 모든 경로의 단일 출처.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "data");

    /// <summary>로드 순서(별칭 합집합 병합): 다국어본 → 영어 보강 → 품질태그 → 한국어 보강.
    /// ko-aliases.csv는 마지막에 두어 기존 태그에 한국어 별칭을 더한다.</summary>
    public static IReadOnlyList<string> BundledCsvFiles { get; } = new[]
    {
        Path.Combine(DataDir, "danbooru_tags.csv"),
        Path.Combine(DataDir, "danbooru.csv"),
        Path.Combine(DataDir, "extra-quality-tags.csv"),
        Path.Combine(DataDir, "ko-aliases.csv"),
        Path.Combine(DataDir, "ko-categories.csv"),
        Path.Combine(DataDir, "ko-nsfw.csv"),
    };

    /// <summary>모순 태그 규칙 파일(상호배타 그룹). data/ 동봉.</summary>
    public static string ConflictsFile { get; } = Path.Combine(DataDir, "conflicts.csv");

    /// <summary>프로그램 제공 프리셋(번들 축 풀·컨셉 팩). 앱 시작 시 PresetSeeder가
    /// 사용자 데이터(%APPDATA%)로 한 번만 주입한다 — 깃에는 이 번들만 올라가고
    /// 사용자 커스텀은 올라가지 않는 구조의 경계선.</summary>
    public static string PresetPoolsFile { get; } = Path.Combine(DataDir, "presets", "pools.json");
    public static string PresetRecipesDir { get; } = Path.Combine(DataDir, "presets", "recipes");

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DanbooruTagGen");

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");
    public static string PoolsFile => Path.Combine(AppDataDir, "pools.json");
    /// <summary>마지막으로 작업하던 레시피(슬롯 구성)의 자동 저장/복원 위치.
    /// Pool과 달리 이전엔 이 경로에 아무도 쓰거나 읽지 않아 앱을 재시작하면 레시피가 사라졌다.</summary>
    public static string LastRecipeFile => Path.Combine(AppDataDir, "last-recipe.json");
    /// <summary>이름 붙여 저장한 여러 레시피 프리셋 목록(레시피 라이브러리). LastRecipeFile은
    /// "마지막 작업 상태" 하나뿐이고, 이쪽은 Pool처럼 여러 개를 골라 쓸 수 있게 한다.</summary>
    public static string RecipesFile => Path.Combine(AppDataDir, "recipes.json");

    public static void EnsureAppDataDir() => Directory.CreateDirectory(AppDataDir);
}
