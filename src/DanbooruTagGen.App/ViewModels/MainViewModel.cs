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
    /// <summary>파일 감시 상태(도구 메뉴에 표시).</summary>
    [ObservableProperty] private string _watchStateText = "";
    /// <summary>마지막 프리셋 갱신 시각(상태 표시줄).</summary>
    [ObservableProperty] private string _lastSyncText = "";
    /// <summary>마지막 번들 프리셋 읽기가 실패했으면 그 이유(성공하면 지운다). 상태 메시지와 달리 고칠 때까지 남는다.</summary>
    [ObservableProperty] private string _presetLoadError = "";

    /// <summary>메인 창 탭: 0 = 일괄 생성(기본), 1 = 레시피 편집. 생성 카드의 대상이 탭을 따라간다(일괄 = 체크한 레시피, 편집 = 빌더 구성).</summary>
    [ObservableProperty] private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (Generation is { } g) g.IsMultiRecipeMode = value == 0;
    }

    /// <summary>레시피 편집 탭으로(불러오기·컨셉 빌더 보내기 뒤).</summary>
    public void ShowEditTab() => SelectedTabIndex = 1;

    /// <summary>지운 제공 팩 창(도구 ▾).</summary>
    [RelayCommand]
    private void OpenDeletedPresets() =>
        new Views.DeletedPresetsWindow(this) { Owner = System.Windows.Application.Current.MainWindow }.Show();

    private Views.LogWindow? _logWindow;

    /// <summary>문제 로그 창(도구 ▾ → 로그…). 예전 '상태 기록'(📜)을 대신한다 — 평범한 상태 문장은 남기지 않고 문제만.</summary>
    [RelayCommand]
    private void OpenLog()
    {
        if (_logWindow != null) { _logWindow.Activate(); return; }
        var win = new Views.LogWindow() { Owner = System.Windows.Application.Current.MainWindow };
        _logWindow = win;
        win.Closed += (_, _) => _logWindow = null;
        win.Show();
    }

    /// <summary>개발 모드 켜기/끄기(다음 실행부터). 상태 표시줄 체크박스.</summary>
    public bool DevModeSetting
    {
        get => Settings.DevModeReadRepoData;
        set
        {
            if (Settings.DevModeReadRepoData == value) return;
            Settings.DevModeReadRepoData = value;
            SettingsStore.Save(Settings);
            OnPropertyChanged();
            Status = value == AppPaths.DevMode
                ? (value ? "개발 모드 켜짐(이미 적용 중)." : "개발 모드 꺼짐.")
                : (value ? "개발 모드: 다음 실행부터 저장소의 data/를 직접 읽습니다." : "개발 모드: 다음 실행부터 exe 옆 data/를 읽습니다.");
        }
    }

    public bool DevModeAvailable => AppPaths.RepoRoot != null;
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
        // 그날 첫 실행이면 손대기 전(시딩 전) 상태를 백업한다. 백업 실패로 앱 시작이 막히면 안 된다.
        TryBackup(() => BackupStore.CreateDailyIfMissing(AppPaths.AppDataDir, AppPaths.BackupsDir, DateTime.Now));
        // 문제 로그: 7일 지난 파일을 지우고, 쓰기 실패는 상태 표시줄로.
        AppLog.Log.Prune(DateTime.Now);
        AppLog.WriteFailed += message => Status = message;
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
            AppLog.Program("번들 프리셋 읽기 실패(시작)", error);
            PresetLoadError = "⚠ 번들 읽기 실패: " + error;
            return;
        }
        var seeded = new HashSet<string>(Settings.SeededPresetIds, StringComparer.Ordinal);
        var idsBefore = SavedRecipes.Select(r => r.Id).ToList();
        // 처음 설치(장부가 빔)면 전부가 새것이라 🆕 표시가 의미 없다 — 표시는 그 다음 실행부터.
        bool firstInstall = Settings.SeededPresetIds.Count == 0;
        RecipeChangeTracker.Prune(Settings.RecipeChangeMarks, idsBefore, DateTime.Now);
        if (PresetSeeder.Seed(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded))
        {
            if (!firstInstall) RecipeChangeTracker.MarkNew(Settings.RecipeChangeMarks, idsBefore, SavedRecipes, DateTime.Now);
            CommitSeeding(seeded);
        }
    }

    /// <summary>백업을 만들고 오래된 것을 정리한다. 실패(디스크·권한)는 상태 표시줄에만 남긴다 — 백업 때문에 본 작업이 막히면 안 된다.
    /// 반환: 백업이 실제로 만들어졌는가.</summary>
    private bool TryBackup(Func<string?> create)
    {
        try
        {
            var made = create() != null;
            BackupStore.Prune(AppPaths.BackupsDir);
            return made;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            Status = "⚠ 자동 백업 실패 — " + ex.Message;
            AppLog.Exception("자동 백업 실패", ex);
            return false;
        }
    }

    /// <summary>지금 상태 스냅샷(위험 작업 직전). 반환: 만들어졌는가.</summary>
    public bool Snapshot(string label) =>
        TryBackup(() => BackupStore.CreateSnapshot(AppPaths.AppDataDir, AppPaths.BackupsDir, label, DateTime.Now));

    private Views.BackupWindow? _backupWindow;

    [RelayCommand]
    private void OpenBackups()
    {
        if (_backupWindow != null) { _backupWindow.Activate(); return; }
        var win = new Views.BackupWindow(this) { Owner = System.Windows.Application.Current.MainWindow };
        _backupWindow = win;
        win.Closed += (_, _) => _backupWindow = null;
        win.Show();
    }

    /// <summary>백업 시점으로 되돌린다: 다른 창을 닫고(각 창의 자동 저장이 지금 끝나게) → 지금 상태를 "복원 전"으로 스냅샷 →
    /// 파일을 되돌리고 → 쓰기를 얼린 채(AppWriteGuard) 앱을 다시 시작한다. 메모리의 옛 상태로 다시 덮어쓰지 않으려고 재시작한다.</summary>
    public void RestoreBackup(BackupEntry entry)
    {
        var ok = System.Windows.MessageBox.Show(
            $"{entry.Time:yyyy-MM-dd HH:mm} · {entry.Label}\n\n이 시점의 레시피·풀·훅·설정으로 되돌리고 앱을 다시 시작합니다.\n" +
            "지금 상태는 '복원 전' 백업으로 먼저 남겨 두므로, 잘못 골랐으면 그걸로 다시 되돌릴 수 있습니다.\n\n계속할까요?",
            "백업 복원", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (ok != System.Windows.MessageBoxResult.OK) return;

        var app = System.Windows.Application.Current;
        foreach (var w in app.Windows.OfType<System.Windows.Window>().Where(w => w != app.MainWindow).ToList()) w.Close();
        // 창 닫기 저장이 끝난 뒤의 상태가 "지금 상태"다.
        if (app.MainWindow is MainWindow main) main.SaveOnClose();
        if (!Snapshot("복원 전"))
        {
            System.Windows.MessageBox.Show("지금 상태를 백업하지 못해 복원을 멈췄습니다. " + Status, "백업 복원",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        AppWriteGuard.Freeze();
        try { BackupStore.Restore(entry.Path, AppPaths.AppDataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            System.Windows.MessageBox.Show("복원 중 오류: " + ex.Message + "\n\n앱을 닫습니다. '복원 전' 백업이 남아 있으니 다시 실행해 백업 창에서 되돌릴 수 있습니다.",
                "백업 복원", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            app.Shutdown();
            return;
        }
        try
        {
            if (Environment.ProcessPath is { } exe) System.Diagnostics.Process.Start(exe);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show("복원했습니다. 앱을 다시 실행해 주세요.", "백업 복원");
        }
        app.Shutdown();
    }

    /// <summary>한 번 시딩됐다가 사용자가 지운 제공 팩(번들에는 아직 있음). 장부(seededIds)가 기억해서 다시는 저절로 안 들어온다.
    /// 번들을 못 읽으면 null.</summary>
    public IReadOnlyList<Recipe>? DeletedBundledRecipes(out string error)
    {
        if (!BundledPresetLoader.TryLoad(AppPaths.PresetPoolsFile, AppPaths.PresetRecipesDir, out var bundled, out error)) return null;
        var present = SavedRecipes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var presentNames = SavedRecipes.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var seeded = new HashSet<string>(Settings.SeededPresetIds, StringComparer.Ordinal);
        // 이름이 같은 사본이 있으면(id 도입 전 설치본 등) 지운 게 아니다 — PresetSeeder.Seed와 같은 기준.
        return bundled.Recipes.Where(r => seeded.Contains(r.Id) && !present.Contains(r.Id) && !presentNames.Contains(r.Name)).ToList();
    }

    /// <summary>지운 제공 팩을 다시 넣는다(목록 맨 뒤, 🆕 표시). 장부는 그대로(이미 시딩된 것).</summary>
    public int RestoreBundledRecipes(IEnumerable<Recipe> recipes)
    {
        int n = 0;
        var now = DateTime.Now;
        foreach (var r in recipes)
        {
            if (SavedRecipes.Any(s => s.Id == r.Id)) continue;
            SavedRecipes.Add(r);
            Settings.RecipeChangeMarks[r.Id] = new RecipeChangeMark { Kind = RecipeChangeKind.New, At = now };
            n++;
        }
        if (n == 0) return 0;
        SaveRecipeLibrary();
        SettingsStore.Save(Settings);
        RecipeLibrary?.Refresh();
        Generation?.RefreshBatchRecipes();
        Status = $"지운 제공 팩 {n}개를 다시 넣었습니다(라이브러리에 🆕로 표시).";
        return n;
    }

    /// <summary>레시피를 썼다(생성·불러오기) — 라이브러리 "최근 사용순" 정렬용. 설정은 앱을 닫을 때 저장된다.</summary>
    public void MarkRecipesUsed(IEnumerable<string?> ids)
    {
        var now = DateTime.Now;
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id)) Settings.RecipeLastUsed[id] = now;
    }

    /// <summary>레시피 버전을 남긴다(열 때·고칠 때·프리셋 갱신 전후). 같은 내용이면 건너뛰고, 짧은 연속 편집은 합친다.
    /// 실패해도 본 작업을 막지 않는다(상태 표시줄에만).</summary>
    public void RecordRecipeVersion(Recipe recipe, string reason) =>
        RecordRecipeVersion(RecipeHistoryStore.Serialize(recipe), recipe.Id, recipe.Name, reason);

    /// <summary>미리 직렬화해 둔 내용(선택할 때 잡아 둔 "고치기 전")을 버전으로 남긴다.</summary>
    public void RecordRecipeVersion(string recipeJson, string recipeId, string recipeName, string reason)
    {
        if (AppWriteGuard.Frozen) return;
        try { RecipeHistoryStore.Record(AppPaths.HistoryDir, recipeJson, recipeId, reason, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "⚠ 레시피 버전 기록 실패 — " + ex.Message;
            AppLog.Exception($"레시피 버전 기록 실패 — {recipeName}", ex);
        }
    }

    /// <summary>목록 배지(🆕 새로 들어옴 / ✏️ 바뀜, 아직 안 열어 본 것만).</summary>
    public string ChangeBadgeOf(string id) => RecipeChangeTracker.Badge(Settings.RecipeChangeMarks, id);

    /// <summary>레시피를 열어 봤다 — 🆕/✏️ 표시를 지운다(설정은 앱을 닫을 때 저장된다). 반환: 표시가 있었는가.</summary>
    public bool MarkRecipeSeen(string id) => Settings.RecipeChangeMarks.Remove(id);

    public void ClearRecipeChangeMarks()
    {
        Settings.RecipeChangeMarks.Clear();
        SettingsStore.Save(Settings);
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
        // 셋은 서로 독립이라 같이 읽는다(예전엔 차례로 읽어 태그 사전 + 규칙 시간이 그대로 더해졌다).
        var dbTask = Task.Run(() => TagDatabase.LoadFromFiles(csvPaths));
        var conflictsTask = Task.Run(() => Core.Generation.ConflictRules.LoadFromFile(AppPaths.ConflictsFile));
        var requiresTask = Task.Run(() => Core.Generation.RequiresRules.LoadFromFileOrDefault(AppPaths.RequiresFile));
        var db = await dbTask;
        Conflicts = await conflictsTask;
        Requires = await requiresTask;
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
        // 일괄 생성 탭으로 시작한다(생성 대상 = 체크한 레시피).
        Generation.IsMultiRecipeMode = SelectedTabIndex == 0;

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
        _rulesWatcher.Error += (_, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Status = "⚠ 규칙 파일 감시가 멈췄습니다 — 앱을 다시 시작하세요: " + e.GetException().Message;
            AppLog.Exception("규칙 파일 감시 멈춤", e.GetException());
        });
        RefreshWatchState();
    }

    private void RefreshWatchState()
    {
        var parts = new List<string>();
        if (_presetWatcher != null) parts.Add("프리셋");
        if (_rulesWatcher != null) parts.Add("규칙");
        WatchStateText = (parts.Count == 0 ? "⚠ 파일 감시 꺼짐" : "👁 " + string.Join("·", parts) + " 감시 중")
            + (AppPaths.DevMode ? " (저장소 data)" : " (exe 옆 data)");
    }

    private async void ReloadRules()
    {
        try
        {
            var conflicts = await Task.Run(() => Core.Generation.ConflictRules.LoadFromFile(AppPaths.ConflictsFile));
            var requires = await Task.Run(() => Core.Generation.RequiresRules.LoadFromFileOrDefault(AppPaths.RequiresFile));
            Conflicts = conflicts;
            Requires = requires;
            // 규칙을 소비하는 쪽은 다음 생성부터 새 규칙을 읽지만, 이미 화면에 떠 있는 경고·배지는 옛 규칙으로
            // 계산된 채 남는다 — 방금 고친 규칙이 반영됐는지 확인하려면 재시작해야 했다.
            RecipeBuilder?.RefreshConflicts();
            RecipeLibrary?.RefreshConflictBadges();
            Status = $"모순 규칙 {conflicts.Count}개를 다시 읽었습니다";
        }
        catch (Exception ex)
        {
            // 파일을 쓰는 중이면 읽기에 실패할 수 있다 — 기존 규칙을 유지하고 다음 변경 이벤트에서 다시 시도한다.
            // 다만 조용히 넘기면 "고쳤는데 왜 반영이 안 되지"가 되므로 이유는 남긴다.
            Status = "모순 규칙을 다시 읽지 못해 기존 규칙을 유지합니다(파일을 다시 저장하면 재시도) — " + ex.Message;
            // 파일 잠금(IOException)은 편집기가 쓰는 도중에 늘 생기는 일시적 실패라 로그에 안 남긴다 — 내용 오류만 남긴다.
            if (ex is not IOException) AppLog.Exception("모순 규칙 다시 읽기 실패", ex);
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
            _presetWatcher.Error += (_, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Status = "⚠ 프리셋 폴더 감시가 멈췄습니다 — ⚡ 퀵 갱신을 누르거나 앱을 다시 시작하세요: " + e.GetException().Message;
                AppLog.Exception("프리셋 폴더 감시 멈춤", e.GetException());
            });
        }
        RefreshWatchState();
    }

    // 백업 복원 후 재시작 중(AppWriteGuard)이면 아무것도 안 쓴다 — 방금 되돌린 파일을 옛 상태로 덮어쓰지 않게.
    public void SavePools() { if (!AppWriteGuard.Frozen) PoolStore.Save(Pools, AppPaths.PoolsFile); }
    public void SaveRecipeLibrary() { if (!AppWriteGuard.Frozen) RecipeLibraryStore.Save(SavedRecipes, AppPaths.RecipesFile); }
    public void SaveHooks() { if (!AppWriteGuard.Frozen) HookStore.Save(Hooks, AppPaths.HooksFile); }
    public void SaveBatchPresets() { if (!AppWriteGuard.Frozen) BatchPresetStore.Save(BatchPresets, AppPaths.BatchPresetsFile); }

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

        // 미리 돌려 보고(사본) 무엇이 바뀌는지 이름으로 보여 준다 — 예전엔 일반 안내문뿐이라 무엇이 지워질지 몰랐다.
        var (added, replaced, removed, poolChanges) = PresetSeeder.Preview(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, Settings.SeededPresetIds);
        if (added.Count == 0 && replaced.Count == 0 && removed.Count == 0 && poolChanges == 0)
        {
            System.Windows.MessageBox.Show("바뀔 것이 없습니다 — 라이브러리가 이미 번들과 같습니다.", "프리셋 갱신",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        static string Names(IReadOnlyList<string> names) =>
            names.Count == 0 ? "없음" : string.Join("\n    ", names.Take(12)) + (names.Count > 12 ? $"\n    … 외 {names.Count - 12}개" : "");
        var ok = System.Windows.MessageBox.Show(
            "프로그램 제공 축 풀·컨셉 팩을 최신 내용으로 갱신합니다.\n\n" +
            $"➕ 추가 {added.Count}개:\n    {Names(added)}\n\n" +
            $"✏️ 내용 교체 {replaced.Count}개(이 팩을 앱에서 고쳤다면 그 수정은 덮어써집니다 — 고치기 전 버전은 🕘 버전에 남습니다):\n    {Names(replaced)}\n\n" +
            $"🗑 제거 {removed.Count}개(번들에서 완전히 빠진 팩):\n    {Names(removed)}\n\n" +
            (poolChanges > 0 ? $"축 풀 변경 {poolChanges}개\n\n" : "") +
            "직접 만든 풀·레시피는 그대로 둡니다. 갱신 직전 상태는 🗄 백업에 스냅샷으로 남습니다.\n\n계속할까요?",
            "프리셋 갱신", System.Windows.MessageBoxButton.OKCancel,
            removed.Count > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question);
        if (ok != System.Windows.MessageBoxResult.OK) return;

        Snapshot("프리셋 갱신 전");
        ApplyBundledSync(bundled, alreadyBackedUp: true);
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
            AppLog.Program("번들 프리셋 읽기 실패(자동 갱신)", error + "\n기존 데이터는 그대로 두고 건너뜀. 파일을 고쳐 저장하면 자동으로 다시 읽음.");
            PresetLoadError = "⚠ 번들 읽기 실패: " + error;
            return;
        }
        _presetSyncRetry.OnSucceeded();
        PresetLoadError = "";
        ApplyBundledSync(bundled, removalLimit: MaxAutoRemovals);
    }

    /// <summary>Seed+SyncBundled 실행, 화면 갱신, Status 문구 설정까지 — 확인/완료 UI는 호출부 책임.
    /// <paramref name="removalLimit"/>보다 많이 제거하게 되면 제거만 보류한다(자동 경로용 안전판).</summary>
    private void ApplyBundledSync(BundledPresets bundled, int removalLimit = int.MaxValue, bool alreadyBackedUp = false)
    {
        if (AppWriteGuard.Frozen) return;   // 백업 복원 후 재시작 중
        var previousLedger = Settings.SeededPresetIds.ToList();
        var seeded = new HashSet<string>(previousLedger, StringComparer.Ordinal);
        var idsBefore = SavedRecipes.Select(r => r.Id).ToList();
        var added = PresetSeeder.Seed(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded);
        int pendingRemovals = PresetSeeder.CountPendingRemovals(Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded);
        bool holdRemovals = pendingRemovals > removalLimit;
        // 사용자 데이터에서 무언가 지워질 참이면(번들에서 빠진 팩) 지우기 전 상태를 남긴다.
        if (pendingRemovals > 0 && !holdRemovals && !alreadyBackedUp) Snapshot($"제거 전 ({pendingRemovals}개)");
        var changedRecipeIds = new HashSet<string>(StringComparer.Ordinal);
        // 갱신으로 바뀌기 전 내용을 버전으로 남기려고 지금 객체를 잡아 둔다(SyncBundled는 목록 칸을 새 객체로 갈아 끼울 뿐 옛 객체를 고치지 않는다).
        var beforeSync = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        foreach (var r in SavedRecipes) beforeSync.TryAdd(r.Id, r);
        var (updated, removed) = PresetSeeder.SyncBundled(
            Pools, bundled.Pools, SavedRecipes, bundled.Recipes, seeded, allowRemoval: !holdRemovals, changedRecipeIds);
        int newRecipes = 0;
        if (added || updated > 0 || removed > 0)
        {
            // 🆕/✏️ 표시: 장부 저장보다 먼저 메모리에 반영해 두고, 장부와 함께(또는 맨 끝에) 저장한다.
            var now = DateTime.Now;
            newRecipes = RecipeChangeTracker.MarkNew(Settings.RecipeChangeMarks, idsBefore, SavedRecipes, now);
            var stillThere = SavedRecipes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            RecipeChangeTracker.MarkUpdated(Settings.RecipeChangeMarks, changedRecipeIds.Where(stillThere.Contains), now);
            RecipeChangeTracker.Prune(Settings.RecipeChangeMarks, stillThere, now);
            foreach (var id in changedRecipeIds)
            {
                if (beforeSync.TryGetValue(id, out var old)) RecordRecipeVersion(old, "프리셋 갱신 전");
                if (SavedRecipes.FirstOrDefault(r => r.Id == id) is { } fresh) RecordRecipeVersion(fresh, "프리셋 갱신");
            }

            // 장부(seededIds) 저장 순서는 PresetSeeder.PlanLedgerWrites 참고:
            // 제거분은 데이터보다 먼저 장부에서 빼고, 추가분은 데이터 뒤에 장부에 넣는다.
            var (ledgerBefore, ledgerAfter) = PresetSeeder.PlanLedgerWrites(previousLedger, seeded);
            if (ledgerBefore != null) SaveLedger(ledgerBefore);
            SavePools();
            SaveRecipeLibrary();
            if (ledgerAfter != null) SaveLedger(ledgerAfter);
            else SettingsStore.Save(Settings);   // 장부는 그대로여도 🆕/✏️ 표시는 바뀌었을 수 있다
            // 화면이 옛 객체를 들고 있으면 창을 닫을 때 그 사본이 되쓰여 갱신이 무효가 된다.
            // (뷰모델은 InitializeAsync 뒤에야 생기므로 null 가드)
            RecipeBuilder?.RefreshPools();
            RecipeBuilder?.RefreshConflicts();   // 풀 후보가 바뀌었으면 빌더의 모순·다양성 경고도 달라진다
            PoolLibrary?.Refresh();
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
        LastSyncText = $"프리셋 확인 {DateTime.Now:HH:mm:ss}";
        Status = (added || updated > 0 || removed > 0
                     ? $"프리셋 갱신 완료 — 기존 {updated}개 교체, 제거 {removed}개, 신규 항목 {(added ? "추가됨" : "없음")}."
                       + (newRecipes > 0 ? $" 새 레시피 {newRecipes}개는 라이브러리에 🆕로 표시됩니다." : "")
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
