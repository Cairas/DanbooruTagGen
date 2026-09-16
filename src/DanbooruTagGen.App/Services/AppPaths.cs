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

    /// <summary>개발 저장소의 원본 data/presets/recipes 폴더(있으면). exe는 보통
    /// {repo}/src/DanbooruTagGen.App/bin/{Config}/{TFM}/ 밑에서 돌아가므로, 그 자리에서
    /// 5단계 위로 올라가면 저장소 루트다. 이 프로젝트는 1인 로컬 개발용이라 exe 옆 사본과
    /// 저장소 원본이 같은 체크아웃 안에 공존한다 — 번들 팩을 앱에서 직접 고치면 그 수정이
    /// exe 옆 사본에서 끝나지 않고 저장소 원본에도 반영되도록 여기를 함께 쓴다.
    /// 저장소 구조를 벗어난 배포 환경(패키징된 설치본 등)에서는 그 경로에 폴더가 없을 테니
    /// null — 호출부가 있을 때만 추가로 쓴다.</summary>
    public static string? RepoSourceRecipesDir { get; } = ResolveRepoSourceRecipesDir();

    private static string? ResolveRepoSourceRecipesDir()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is null) return null;
        // net9.0-windows -> Release/Debug -> bin -> DanbooruTagGen.App -> src -> repo root
        var repoRoot = Directory.GetParent(exeDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
        if (repoRoot is null) return null;
        var candidate = Path.Combine(repoRoot, "data", "presets", "recipes");
        return Directory.Exists(candidate) ? candidate : null;
    }

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
