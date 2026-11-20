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

    /// <summary>저장소 루트(있으면). exe는 보통 {repo}/src/DanbooruTagGen.App/bin/{Config}/{TFM}/ 밑에서 돈다.</summary>
    public static string? RepoRoot { get; } = ResolveRepoRoot();

    /// <summary>개발 모드: 프리셋(data/presets)과 규칙(conflicts.csv·requires.csv)을 exe 옆 사본 대신 저장소의 data/에서 직접 읽고 감시한다 —
    /// 데이터만 고쳤을 때 빌드 출력 폴더로 복사하지 않아도 바로 반영된다. 설정(devModeReadRepoData)으로 켜며 다음 실행부터 적용된다.
    /// 기본 꺼짐: 켜면 저장소에 넣은 작성 중인 레시피 JSON이 곧바로 시딩되므로, 검증 뒤에 링크하던 작업 흐름이 바뀐다.</summary>
    public static bool DevMode { get; } = RepoRoot != null && ReadDevModeSetting();

    /// <summary>프리셋·규칙을 읽는 data 폴더(개발 모드면 저장소, 아니면 exe 옆).</summary>
    public static string PresetDataDir => DevMode ? Path.Combine(RepoRoot!, "data") : DataDir;

    /// <summary>모순 태그 규칙 파일(상호배타 그룹). data/ 동봉.</summary>
    public static string ConflictsFile => Path.Combine(PresetDataDir, "conflicts.csv");

    /// <summary>상대 필요 규칙 파일(requires|태그|any-of|상대…). data/ 동봉 — 없으면 Core 내장본을 쓴다.</summary>
    public static string RequiresFile => Path.Combine(PresetDataDir, "requires.csv");

    /// <summary>프로그램 제공 프리셋(번들 축 풀·컨셉 팩). 앱 시작 시 PresetSeeder가
    /// 사용자 데이터(%APPDATA%)로 한 번만 주입한다 — 깃에는 이 번들만 올라가고
    /// 사용자 커스텀은 올라가지 않는 구조의 경계선.</summary>
    public static string PresetPoolsFile => Path.Combine(PresetDataDir, "presets", "pools.json");
    public static string PresetRecipesDir => Path.Combine(PresetDataDir, "presets", "recipes");

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
        if (ResolveRepoRoot() is not { } repoRoot) return null;
        var candidate = Path.Combine(repoRoot, "data", "presets", "recipes");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveRepoRoot()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is null) return null;
        // net9.0-windows -> Release/Debug -> bin -> DanbooruTagGen.App -> src -> repo root
        var repoRoot = Directory.GetParent(exeDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
        return repoRoot != null && Directory.Exists(Path.Combine(repoRoot, "data", "presets", "recipes")) ? repoRoot : null;
    }

    /// <summary>settings.json의 devModeReadRepoData. 경로 상수가 정해지기 전(SettingsStore보다 먼저)에 필요해 JSON을 직접 읽는다.</summary>
    private static bool ReadDevModeSetting()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DanbooruTagGen", "settings.json");
            if (!File.Exists(path)) return false;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("devModeReadRepoData", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>빌드 시각: 앱·Core 어셈블리 중 늦게 쓰인 쪽. 구빌드로 테스트하는 일을 막으려고 제목 표시줄에 띄운다.</summary>
    public static DateTime BuildTime { get; } = new[] { "DanbooruTagGen.App.dll", "DanbooruTagGen.Core.dll" }
        .Select(f => Path.Combine(AppContext.BaseDirectory, f))
        .Where(File.Exists)
        .Select(File.GetLastWriteTime)
        .DefaultIfEmpty(DateTime.MinValue)
        .Max();

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

    /// <summary>생성 직전에 레시피에 끼워 넣는 사용자 정의 슬롯(훅) 목록.</summary>
    public static string HooksFile => Path.Combine(AppDataDir, "hooks.json");

    /// <summary>"여러 레시피 일괄 생성"에서 고른 조합을 이름 붙여 저장한 프리셋들.</summary>
    public static string BatchPresetsFile => Path.Combine(AppDataDir, "batch-presets.json");

    /// <summary>문제 로그 폴더(하루 한 파일, 7일 보관) — Core.Logging.ProblemLog.</summary>
    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    /// <summary>실제 토크나이저 파일(clip/, qwen3/) — exe 옆 data/tokenizers.</summary>
    public static string TokenizersDir => Path.Combine(DataDir, "tokenizers");

    /// <summary>사용자 데이터 자동 백업(zip) 폴더 — Core.Persistence.BackupStore.</summary>
    public static string BackupsDir => Path.Combine(AppDataDir, "backups");
    /// <summary>레시피별 버전 기록 폴더 — Core.Persistence.RecipeHistoryStore.</summary>
    public static string HistoryDir => Path.Combine(AppDataDir, "history");
    /// <summary>생성 기록 폴더 — Core.Persistence.GenerationHistoryStore.</summary>
    public static string GenerationHistoryDir => Path.Combine(AppDataDir, "generations");

    public static void EnsureAppDataDir() => Directory.CreateDirectory(AppDataDir);
}
