using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using DanbooruTagGen.Core.Tags;

namespace DanbooruTagGen.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "준비됨";
    [ObservableProperty] private bool _isLoading = true;

    public Settings Settings { get; }
    public List<Pool> Pools { get; private set; } = new();
    /// <summary>이름 붙여 저장한 레시피(슬롯 구성 전체) 프리셋 목록 — RecipeBuilder가 관리하는
    /// "마지막 작업 상태" 하나짜리 자동저장(LastRecipeFile)과는 별개로, Pool처럼 여러 개를
    /// 저장해 놓고 골라 쓸 수 있게 한다.</summary>
    public List<Recipe> SavedRecipes { get; private set; } = new();
    /// <summary>생성 직전에 레시피에 끼워 넣는 사용자 정의 슬롯들. 레시피·풀과 달리 생성할
    /// 때만 합쳐지고 레시피 자체에는 남지 않는다.</summary>
    public List<GenerationHook> Hooks { get; private set; } = new();

    /// <summary>일괄 생성 선택 조합 프리셋. 훅과는 독립적으로 저장·불러오기한다.</summary>
    public List<BatchSelectionPreset> BatchPresets { get; private set; } = new();

    /// <summary>모순 태그 규칙(상호배타 그룹). 시작 시 data/conflicts.csv에서 로드.</summary>
    public Core.Generation.ConflictRules Conflicts { get; private set; } = Core.Generation.ConflictRules.Empty;
    /// <summary>상대 필요 규칙. 시작 시 data/requires.csv에서 로드(없으면 빌드에 내장된 같은 파일).
    /// tools/conflict_scan.py와 같은 표를 읽는다.</summary>
    public Core.Generation.RequiresRules Requires { get; private set; } = Core.Generation.RequiresRules.Default;
    /// <summary>태그 빈도·그룹 메타데이터 조회. 가중 추첨/순서 자동 배치에 생성기로 주입.
    /// 태그 DB 자체(TagDatabase)가 ITagLookup을 구현한다.</summary>
    public Core.Generation.ITagLookup? TagInfo { get; private set; }
    /// <summary>태그 DB 원본 참조. 컨셉 빌더 위저드가 테마 검색(SearchTheme)에 쓴다.</summary>
    public TagDatabase? TagDb { get; private set; }

    public TagSearchViewModel TagSearch { get; private set; } = default!;
    public RecipeBuilderViewModel RecipeBuilder { get; private set; } = default!;
    public GenerationViewModel Generation { get; private set; } = default!;
    public PoolLibraryViewModel PoolLibrary { get; private set; } = default!;
    public RecipeLibraryViewModel RecipeLibrary { get; private set; } = default!;
    public HookLibraryViewModel HookLibrary { get; private set; } = default!;

    public MainViewModel()
    {
        AppPaths.EnsureAppDataDir();
        Settings = SettingsStore.Load();
        Pools = PoolStore.Load(AppPaths.PoolsFile);
        SavedRecipes = RecipeLibraryStore.Load(AppPaths.RecipesFile);
        Hooks = HookStore.Load(AppPaths.HooksFile);
        BatchPresets = BatchPresetStore.Load(AppPaths.BatchPresetsFile);
        SeedBundledPresets();
    }

    /// <summary>프로그램 제공 프리셋(data/presets)을 사용자 데이터에 시딩한다.
    /// 새 프리셋만 들어오고, 사용자가 지운 프리셋은 부활하지 않는다(PresetSeeder 참고).
    /// 번들 파일이 없거나 손상돼도 LoadOrDefault가 빈 목록을 주므로 앱 시작은 막히지 않는다.</summary>
    private void SeedBundledPresets()
    {
        // 번들 읽기 실패(작성 중인 파일 잠금·id 중복 등)로 앱 시작 자체가 죽지 않게 한다.
        // 실패하면 이번 실행에선 시딩을 건너뛸 뿐이고, 원인은 상태 표시줄에 남는다.
        if (!BundledPresetLoader.TryLoad(AppPaths.PresetPoolsFile, AppPaths.PresetRecipesDir, out var bundled, out var error))
        {
            Status = "번들 프리셋을 읽지 못해 시딩을 건너뜁니다 — " + error;
            return;
        }
        var seeded = new HashSet<string>(Settings.SeededPresetIds, StringComparer.Ordinal);
        if (PresetSeeder.Seed(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded))
            CommitSeeding(seeded);
    }

    /// <summary>시딩 결과를 디스크에 쓴다. <b>순서가 중요하다</b> — 실제 데이터(풀·레시피)를
    /// 먼저 쓰고, "이미 주입했다"는 장부(settings.json의 seededIds)를 맨 마지막에 쓴다.
    /// 반대로 하면 중간에 죽었을 때 장부에만 id가 남아, 그 팩은 재시작해도 영영 안 들어온다
    /// (실제로 팩 하나가 이 상태로 사라져 있었다).</summary>
    private void CommitSeeding(HashSet<string> seeded)
    {
        SavePools();
        SaveRecipeLibrary();
        Settings.SeededPresetIds = seeded.ToList();
        _seededIdLookup = null;
        SettingsStore.Save(Settings);
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        Status = "태그 데이터 로드 중...";
        var csvPaths = SettingsStore.ResolveCsvPaths(Settings);
        var db = await Task.Run(() => TagDatabase.LoadFromFiles(csvPaths));
        Conflicts = await Task.Run(() => Core.Generation.ConflictRules.LoadFromFile(AppPaths.ConflictsFile));
        Requires = await Task.Run(() => Core.Generation.RequiresRules.LoadFromFileOrDefault(AppPaths.RequiresFile));
        TagInfo = db;
        TagDb = db;

        TagSearch = new TagSearchViewModel(db, Settings);
        PoolLibrary = new PoolLibraryViewModel(this);
        RecipeBuilder = new RecipeBuilderViewModel(this);
        RecipeLibrary = new RecipeLibraryViewModel(this);
        Generation = new GenerationViewModel(this);
        // Generation 뒤에 만든다 — 훅 창이 생성 옵션(자동 정렬)을 읽어 경고를 띄운다.
        HookLibrary = new HookLibraryViewModel(this);
        OnPropertyChanged(nameof(TagSearch));
        OnPropertyChanged(nameof(PoolLibrary));
        OnPropertyChanged(nameof(RecipeBuilder));
        OnPropertyChanged(nameof(RecipeLibrary));
        OnPropertyChanged(nameof(Generation));
        OnPropertyChanged(nameof(HookLibrary));

        IsLoading = false;
        Status = $"태그 {db.Count:N0}개 로드됨";

        StartPresetWatcher();
        StartRulesWatcher();
    }

    private FileSystemWatcher? _rulesWatcher;
    private System.Windows.Threading.DispatcherTimer? _rulesReloadDebounce;

    /// <summary>data/conflicts.csv·requires.csv가 바뀌면 앱을 재시작하지 않아도 규칙을 다시 읽는다.
    /// 컨셉 팩을 만들 때 모순 규칙(표식)을 같이 추가하는 일이 잦은데, 예전엔 시작 시 한 번만 읽어서
    /// 규칙을 고칠 때마다 재시작해야 했다. 소비자는 모두 매번 Conflicts/Requires 속성을 읽으므로
    /// 속성만 교체하면 다음 생성·검사부터 새 규칙이 적용된다.</summary>
    private void StartRulesWatcher()
    {
        var dir = Path.GetDirectoryName(AppPaths.ConflictsFile);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        _rulesReloadDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _rulesReloadDebounce.Tick += (_, _) =>
        {
            _rulesReloadDebounce!.Stop();
            ReloadRules();
        };

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!string.Equals(e.Name, Path.GetFileName(AppPaths.ConflictsFile), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(e.Name, Path.GetFileName(AppPaths.RequiresFile), StringComparison.OrdinalIgnoreCase))
                return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _rulesReloadDebounce!.Stop();
                _rulesReloadDebounce.Start();
            });
        }

        _rulesWatcher = new FileSystemWatcher(dir, "*.csv")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _rulesWatcher.Changed += OnChanged;
        _rulesWatcher.Created += OnChanged;
        _rulesWatcher.Renamed += OnChanged;
        _rulesWatcher.EnableRaisingEvents = true;
    }

    private async void ReloadRules()
    {
        try
        {
            var conflicts = await Task.Run(() => Core.Generation.ConflictRules.LoadFromFile(AppPaths.ConflictsFile));
            var requires = await Task.Run(() => Core.Generation.RequiresRules.LoadFromFileOrDefault(AppPaths.RequiresFile));
            Conflicts = conflicts;
            Requires = requires;
            Status = $"모순 규칙 {conflicts.Count}개를 다시 읽었습니다";
        }
        catch
        {
            // 파일을 쓰는 중이면 읽기에 실패할 수 있다 — 기존 규칙을 유지하고 다음 변경 이벤트에서 다시 시도한다.
        }
    }

    private FileSystemWatcher? _presetWatcher;
    private System.Windows.Threading.DispatcherTimer? _presetSyncDebounce;
    /// <summary>번들 읽기 실패 시 자동 갱신 재시도 판단. 스크립트가 파일을 쓰는 중이면 몇십 ms
    /// 뒤엔 성공하지만, JSON 문법 오류·id 중복 같은 제작 실수는 몇 번을 시도해도 그대로라
    /// 변경 하나당 상한을 둔다. 예산은 파일 변경 이벤트마다 리셋된다(PresetSyncRetryGate 참고) —
    /// 편집 중 깨진 저장이 끼어도 다음 유효한 저장에서 앱 재시작 없이 다시 동기화된다.</summary>
    private readonly PresetSyncRetryGate _presetSyncRetry = new(maxRetries: 3);

    /// <summary>자동 경로(워처·퀵 갱신)에서 한 번에 이보다 많이 제거하게 되면 제거만 보류한다.
    /// 번들 일부가 잠깐 안 보이는 순간(폴더 이동·체크아웃 도중)에 대량 삭제되는 걸 막는 안전판.
    /// 보류된 제거는 "⟳ 프리셋 갱신"(확인창 있는 수동 갱신)으로 적용된다.</summary>
    private const int MaxAutoRemovals = 3;

    /// <summary>data/presets(번들 축 풀·컨셉 팩)의 변경을 감지해 자동으로 다시 읽는다. 예전엔
    /// JSON을 스크립트로 고칠 때마다 "⟳ 프리셋 갱신"을 손으로 눌러야 했다 — 이 병합 로직
    /// (PresetSeeder) 자체는 사용자 자작 항목을 보존하는 안전장치가 이미 있으므로 그대로
    /// 재사용하고, 트리거만 자동화한다.</summary>
    private void StartPresetWatcher()
    {
        _presetSyncDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _presetSyncDebounce.Tick += (_, _) =>
        {
            _presetSyncDebounce!.Stop();
            _ = QuickSyncBundledPresetsAsync();
        };

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            // Changed/Created/Deleted/Renamed는 파일 워처 스레드에서 오므로 DispatcherTimer를
            // 건드리기 전에 UI 스레드로 넘긴다.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                // 새 변경 = 새 재시도 예산. 직전 저장이 깨진 JSON이라 재시도를 다 써 버렸어도
                // 이번 저장은 처음부터 다시 시도한다.
                _presetSyncRetry.OnSourceChanged();
                _presetSyncDebounce!.Stop();
                _presetSyncDebounce.Start();
            });
        }

        var presetsDir = Path.GetDirectoryName(AppPaths.PresetPoolsFile);
        if (!string.IsNullOrEmpty(presetsDir) && Directory.Exists(presetsDir))
        {
            _presetWatcher = new FileSystemWatcher(presetsDir, "*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            };
            _presetWatcher.Changed += OnChanged;
            _presetWatcher.Created += OnChanged;
            _presetWatcher.Deleted += OnChanged;
            _presetWatcher.Renamed += OnChanged;
            _presetWatcher.EnableRaisingEvents = true;
        }
    }

    public void SavePools() => PoolStore.Save(Pools, AppPaths.PoolsFile);
    public void SaveRecipeLibrary() => RecipeLibraryStore.Save(SavedRecipes, AppPaths.RecipesFile);
    public void SaveHooks() => HookStore.Save(Hooks, AppPaths.HooksFile);
    public void SaveBatchPresets() => BatchPresetStore.Save(BatchPresets, AppPaths.BatchPresetsFile);

    /// <summary>seededIds(=한 번이라도 번들에서 주입한 id) 조회용 캐시. 라이브러리 목록을
    /// 채울 때 레시피마다 조회하므로 List.Contains(O(n))로 두면 700×200번 훑게 된다.
    /// 시딩/갱신으로 목록이 바뀌면 무효화한다.</summary>
    private HashSet<string>? _seededIdLookup;

    /// <summary>이 항목이 프로그램 제공(번들) 팩에서 온 것인지. 라이브러리에 남아 있는 번들
    /// 항목은 프리셋 갱신 때 최신 내용으로 덮어써지므로, 앱 안에서 고친 내용은 보존되지 않는다.</summary>
    public bool IsBundled(string id) =>
        !string.IsNullOrEmpty(id)
        && (_seededIdLookup ??= new HashSet<string>(Settings.SeededPresetIds, StringComparer.Ordinal)).Contains(id);

    /// <summary>번들 프리셋(축 풀·컨셉 팩)을 최신 내용으로 갱신한다.
    /// 시딩은 "한 번만"이라 프로그램 업데이트로 기존 팩의 태그가 보강돼도, 앱을 재시작하기
    /// 전까지는 아직 한 번도 시딩되지 않은 새 팩도 사용자 데이터엔 반영되지 않는다.
    /// 이 명령이 그 갱신 경로 — 기존 팩 갱신(SyncBundled)과 신규 팩 주입(Seed)을 함께
    /// 수행해 앱을 재시작하지 않아도 방금 추가된 번들 팩이 라이브러리에 들어오게 한다.
    /// 자작 항목과 삭제 상태는 건드리지 않지만, 번들 팩을 직접 고쳤다면 그 수정은
    /// 덮어써지므로 사용자에게 먼저 확인받는다.</summary>
    [RelayCommand]
    private void SyncBundledPresets()
    {
        if (!BundledPresetLoader.TryLoad(AppPaths.PresetPoolsFile, AppPaths.PresetRecipesDir, out var bundled, out var error))
        {
            System.Windows.MessageBox.Show(error, "프리셋 갱신",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var ok = System.Windows.MessageBox.Show(
            "프로그램 제공 축 풀·컨셉 팩을 최신 내용으로 갱신합니다.\n\n" +
            "· 아직 설치되지 않은 새 팩은 추가됩니다.\n" +
            "· 직접 만든 풀·레시피는 그대로 둡니다.\n" +
            "· 예전에 설치됐다가 이번 업데이트로 번들에서 완전히 빠진 팩은 라이브러리에서도 같이 제거됩니다.\n" +
            "· 제공 팩을 직접 수정했다면 그 수정은 덮어써집니다.\n\n계속할까요?",
            "프리셋 갱신", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
        if (ok != System.Windows.MessageBoxResult.OK) return;

        ApplyBundledSync(bundled);
        System.Windows.MessageBox.Show(Status, "프리셋 갱신",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>확인창·완료 팝업 없이 바로 실행하는 프리셋 갱신. 결과는 하단 상태바(Status)에만
    /// 뜬다. 매번 갱신 버튼을 누르는 반복 작업(레시피/풀 스크립트 편집 중 계속 갱신)이 팝업
    /// 두 개 때문에 번거롭다는 요청으로 추가 — 동작(덮어쓰기 포함)은 기존 버튼과 동일하고
    /// 확인 절차만 생략한다.
    /// <para>번들 읽기(파일 수백 개)는 백그라운드에서 하고, 병합·저장·화면 갱신만 UI 스레드에서 한다.
    /// 읽는 동안 워처나 버튼이 또 부르면 겹쳐 돌리지 않고 끝난 뒤 한 번 더 돈다 — 겹치면 앞선 실행의
    /// 옛 번들이 나중에 병합돼 방금 저장한 내용을 되돌릴 수 있다.</para></summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task QuickSyncBundledPresetsAsync()
    {
        if (_quickSyncRunning) { _quickSyncAgain = true; return; }
        _quickSyncRunning = true;
        try
        {
            do
            {
                _quickSyncAgain = false;
                var (ok, bundled, error) = await Task.Run(() =>
                {
                    bool loaded = BundledPresetLoader.TryLoad(AppPaths.PresetPoolsFile, AppPaths.PresetRecipesDir, out var b, out var e);
                    return (loaded, b, e);
                });
                ApplyQuickSync(ok, bundled, error);
            } while (_quickSyncAgain);
        }
        finally { _quickSyncRunning = false; }
    }

    private bool _quickSyncRunning;
    private bool _quickSyncAgain;

    private void ApplyQuickSync(bool loaded, BundledPresets bundled, string error)
    {
        if (!loaded)
        {
            // 워처가 "쓰는 중"인 파일을 잡았거나 편집 도중 JSON이 잠깐 깨진 경우가 대부분이라,
            // 조금 뒤 한 번 더 시도한다. 계속 실패하면 재시도를 멈추고 이유를 남긴다 — 사용자
            // 데이터는 전혀 건드리지 않았으므로, 파일을 고쳐 저장하면 그 변경 이벤트로 다시 돈다.
            if (_presetSyncRetry.TryScheduleRetry())
            {
                _presetSyncDebounce?.Start();
                Status = $"프리셋 갱신 재시도 중({_presetSyncRetry.Retries}/{_presetSyncRetry.MaxRetries})… {error}";
                return;
            }
            Status = "프리셋 갱신 실패(파일을 고쳐 저장하면 자동으로 다시 시도) — " + error;
            return;
        }
        _presetSyncRetry.OnSucceeded();
        ApplyBundledSync(bundled, removalLimit: MaxAutoRemovals);
    }

    /// <summary>Seed+SyncBundled 실행, 화면 갱신, Status 문구 설정까지 — 확인/완료 UI는 호출부 책임.
    /// <paramref name="removalLimit"/>보다 많이 제거하게 되면 제거만 보류한다(자동 경로용 안전판).</summary>
    private void ApplyBundledSync(BundledPresets bundled, int removalLimit = int.MaxValue)
    {
        var previousLedger = Settings.SeededPresetIds.ToList();
        var seeded = new HashSet<string>(previousLedger, StringComparer.Ordinal);
        var added = PresetSeeder.Seed(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded);
        int pendingRemovals = PresetSeeder.CountPendingRemovals(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded);
        bool holdRemovals = pendingRemovals > removalLimit;
        var changedRecipeIds = new HashSet<string>(StringComparer.Ordinal);
        var (updated, removed) = PresetSeeder.SyncBundled(
            Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded, allowRemoval: !holdRemovals, changedRecipeIds);
        if (added || updated > 0 || removed > 0)
        {
            // 장부(seededIds) 저장 순서는 PresetSeeder.PlanLedgerWrites 참고:
            // 제거분은 데이터보다 먼저 장부에서 빼고, 추가분은 데이터 뒤에 장부에 넣는다.
            var (ledgerBefore, ledgerAfter) = PresetSeeder.PlanLedgerWrites(previousLedger, seeded);
            if (ledgerBefore != null) SaveLedger(ledgerBefore);
            SavePools();
            SaveRecipeLibrary();
            if (ledgerAfter != null) SaveLedger(ledgerAfter);
            // 화면이 옛 객체를 들고 있으면 창을 닫을 때 그 사본이 되쓰여 갱신이 무효가 된다.
            // (뷰모델은 InitializeAsync 뒤에야 생기므로 null 가드)
            RecipeBuilder?.RefreshPools();
            RecipeLibrary?.Refresh();
            Generation?.RefreshBatchRecipes();

            // 빌더에 이미 불러와 편집 중인 레시피가 이번 갱신 대상에 포함돼 있으면 화면도
            // 같이 최신으로 맞춘다 — 안 그러면 퀵갱신을 눌러도 빌더에 열어 둔 내용은 그대로
            // 남아 "갱신이 안 먹힌다"는 인상을 준다(2026-08-26).
            // 실제로 바뀐 팩일 때만 — 다른 팩만 바뀌었는데 다시 불러오면 편집 중인 선택·되돌리기 기록이 흔들린다.
            var loadedId = RecipeBuilder?.LoadedRecipeId;
            if (!string.IsNullOrEmpty(loadedId) && changedRecipeIds.Contains(loadedId))
            {
                var fresh = bundled.Recipes.FirstOrDefault(r => r.Id == loadedId);
                if (fresh != null) RecipeBuilder!.LoadRecipe(fresh);
            }
        }
        Status = (added || updated > 0 || removed > 0
                     ? $"프리셋 갱신 완료 — 기존 {updated}개 교체, 제거 {removed}개, 신규 항목 {(added ? "추가됨" : "없음")}."
                     : "프리셋 갱신 — 바뀐 항목 없음.")
                 + (holdRemovals
                     ? $" (번들에서 빠진 항목 {pendingRemovals}개는 자동 제거를 보류 — 의도한 삭제면 \"⟳ 프리셋 갱신\"으로 적용)"
                     : "");
    }

    private void SaveLedger(List<string> ids)
    {
        Settings.SeededPresetIds = ids;
        _seededIdLookup = null;
        SettingsStore.Save(Settings);
    }

    private Views.PoolLibraryWindow? _poolLibraryWindow;

    [RelayCommand]
    private void OpenPoolLibrary()
    {
        // 이미 열려 있으면 새로 안 만들고 앞으로 가져오기만 한다(비모달이라 중복으로 열릴 수 있음).
        if (_poolLibraryWindow != null)
        {
            _poolLibraryWindow.Activate();
            return;
        }

        PoolLibrary.AttachTagSearch();
        var win = new Views.PoolLibraryWindow { DataContext = PoolLibrary, Owner = System.Windows.Application.Current.MainWindow };
        _poolLibraryWindow = win;
        RestoreWindowSize(win, Settings.PoolLibraryWidth, Settings.PoolLibraryHeight);
        // ShowDialog(모달)이었을 땐 이 창이 떠 있는 동안 메인 창(왼쪽 태그 검색 패널)이
        // 완전히 막혀서, AttachTagSearch로 연결해 둔 "검색에서 더블클릭 → 선택한 풀에 추가"가
        // 애초에 실행될 수 없었다(클릭 자체가 안 먹으니까). Show(비모달)로 바꿔 검색 패널과
        // 동시에 쓸 수 있게 하고, 뒷정리는 닫힐 때(Closed)로 옮긴다.
        win.Closed += (_, _) =>
        {
            _poolLibraryWindow = null;
            (Settings.PoolLibraryWidth, Settings.PoolLibraryHeight) = (win.Width, win.Height);
            SettingsStore.Save(Settings);
            PoolLibrary.Persist();
            RecipeBuilder.AttachTagSearch();
            RecipeBuilder.RefreshPools();
        };
        win.Show();
    }

    /// <summary>저장된 창 크기가 있으면 복원한다(0 이하 = 아직 저장된 적 없음 → XAML 기본값).</summary>
    private static void RestoreWindowSize(System.Windows.Window win, double width, double height)
    {
        if (width > 100) win.Width = width;
        if (height > 100) win.Height = height;
    }

    /// <summary>컨셉 빌더 위저드. 자기완결형 만들기 흐름이라 모달로 띄운다
    /// (풀 라이브러리처럼 태그 검색 패널과 동시에 쓸 필요가 없음).</summary>
    [RelayCommand]
    private void OpenConceptWizard()
    {
        if (TagDb == null)
        {
            Status = "태그 데이터 로드가 끝난 뒤에 사용할 수 있습니다.";
            return;
        }
        var win = new Views.ConceptWizardWindow
        {
            DataContext = new ConceptWizardViewModel(this, TagDb),
            Owner = System.Windows.Application.Current.MainWindow,
        };
        win.ShowDialog();
    }

    private Views.RecipeLibraryWindow? _recipeLibraryWindow;

    [RelayCommand]
    private void OpenRecipeLibrary()
    {
        if (_recipeLibraryWindow != null)
        {
            _recipeLibraryWindow.Activate();
            return;
        }

        var win = new Views.RecipeLibraryWindow { DataContext = RecipeLibrary, Owner = System.Windows.Application.Current.MainWindow };
        _recipeLibraryWindow = win;
        RestoreWindowSize(win, Settings.RecipeLibraryWidth, Settings.RecipeLibraryHeight);
        // 왼쪽 목록 패널 너비(GridSplitter로 조절) 복원. FindName은 창이 로드된 뒤에야 값을
        // 주므로 Loaded에서 처리 — 생성자 시점엔 아직 시각 트리가 안 만들어져 있다.
        win.Loaded += (_, _) =>
        {
            if (Settings.RecipeLibraryLeftPanelWidth > 50
                && win.FindName("LeftPanelColumn") is System.Windows.Controls.ColumnDefinition col)
                col.Width = new System.Windows.GridLength(Settings.RecipeLibraryLeftPanelWidth);
        };
        win.Closed += (_, _) =>
        {
            _recipeLibraryWindow = null;
            (Settings.RecipeLibraryWidth, Settings.RecipeLibraryHeight) = (win.Width, win.Height);
            if (win.FindName("LeftPanelColumn") is System.Windows.Controls.ColumnDefinition col)
                Settings.RecipeLibraryLeftPanelWidth = col.Width.Value;
            SettingsStore.Save(Settings);
            RecipeLibrary.Persist();
        };
        win.Show();
    }

    private Views.HookLibraryWindow? _hookLibraryWindow;

    /// <summary>훅 설정 창. 풀·레시피 라이브러리와 같은 패턴 — 모달이 아니라서 창을 열어 둔 채
    /// 미리보기를 돌려 훅 효과를 바로 확인할 수 있다.</summary>
    [RelayCommand]
    private void OpenHookLibrary()
    {
        if (_hookLibraryWindow != null)
        {
            _hookLibraryWindow.Activate();
            return;
        }
        if (HookLibrary is null)
        {
            Status = "태그 데이터 로드가 끝난 뒤에 사용할 수 있습니다.";
            return;
        }

        // 창을 열 때마다 다시 채운다 — 풀 목록이 그사이 바뀌었을 수 있고, 자동 정렬 경고도
        // 지금의 생성 옵션 기준으로 판단해야 한다.
        HookLibrary.Refresh();
        var win = new Views.HookLibraryWindow { DataContext = HookLibrary, Owner = System.Windows.Application.Current.MainWindow };
        _hookLibraryWindow = win;
        win.Closed += (_, _) =>
        {
            _hookLibraryWindow = null;
            HookLibrary.Persist();
        };
        win.Show();
    }
}
