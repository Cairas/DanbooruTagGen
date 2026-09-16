using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>여러 레시피 일괄 생성 모드에서 체크할 수 있는 레시피 한 항목.</summary>
public sealed partial class SelectableRecipeItem : ObservableObject
{
    public SelectableRecipeItem(Recipe recipe)
    {
        Recipe = recipe;
        SearchBlob = BuildSearchBlob(recipe);
    }
    public Recipe Recipe { get; }
    public string Name => Recipe.Name;
    /// <summary>NSFW 레시피는 이름이 🔞로 시작한다(전 팩이 지키는 규칙). 카테고리 문자열은
    /// "감금·강제·논콘"/"최대강도"처럼 제각각이라 판정 기준으로 쓰기 어렵다.</summary>
    public bool IsNsfw => Recipe.Name.StartsWith("🔞", StringComparison.Ordinal);
    /// <summary>"프리뷰" 라벨이 붙은 포즈 레퍼런스 샷(전신 누드 스탠딩 등)인지. "🔞 전체"/
    /// "SFW 전체" 일괄 선택 버튼 둘 다에서 제외하는 용도 — 매번 수동으로 체크 해제해야
    /// 하는 게 번거롭다는 피드백으로 추가했다. SFW로 잘못 편입되면 안 되므로 IsNsfw 자체를
    /// 바꾸지 않고 별도 플래그로 뺐다.</summary>
    public bool IsPreview => Recipe.Labels.Contains("프리뷰");
    [ObservableProperty] private bool _isChecked;

    /// <summary>이름 + Labels + 모든 슬롯/그룹 라벨 + 모든 danbooru 태그를 한 문자열로 모은
    /// 검색용 블롭. "netorare"/"네토라레"를 검색창에 치면 사용자가 따로 라벨을 안 붙였어도
    /// 레시피에 실제로 들어있는 태그나 축 이름(둘 다 컨셉 작업 중 항상 채워짐)으로 찾힌다 —
    /// Labels는 태그·축 이름만으론 못 잡는 서사적 분류(예: "타락")를 보완하는 용도로만 쓴다.</summary>
    public string SearchBlob { get; }

    private static string BuildSearchBlob(Recipe recipe)
    {
        var parts = new List<string> { recipe.Name };
        parts.AddRange(recipe.Labels);
        foreach (var slot in recipe.Slots)
        {
            parts.Add(slot.Label);
            switch (slot)
            {
                case FixedSlot f: parts.AddRange(f.Tags); break;
                case RandomPoolSlot p: parts.AddRange(p.Tags); break;
                case AlternativeSlot a:
                    foreach (var g in a.Groups) { parts.Add(g.Label); parts.AddRange(g.Tags); }
                    break;
            }
        }
        return string.Join('␟', parts);
    }
}

public sealed partial class GenerationViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly WildcardGenerator _generator = new();

    /// <summary>단일(현재 빌더) vs 여러 레시피 일괄 생성. 기본 단일 — 기존 동작 그대로 보존.</summary>
    [ObservableProperty] private bool _isMultiRecipeMode;
    /// <summary>일괄 생성 체크 목록(검색 필터를 거친 표시용). 마스터 목록(_allBatchItems)에서
    /// 다시 골라 채우므로, 검색으로 가렸다 다시 보여도 체크 상태가 유지된다.</summary>
    public ObservableCollection<SelectableRecipeItem> BatchItems { get; } = new();
    private readonly List<SelectableRecipeItem> _allBatchItems = new();
    /// <summary>이름뿐 아니라 태그·축 라벨·Labels까지 훑는다(SelectableRecipeItem.SearchBlob) —
    /// "netorare"/"네토라레"처럼 컨셉과 관련된 말을 치면 그 태그나 축 이름을 가진 레시피가 걸린다.</summary>
    [ObservableProperty] private string _batchSearchText = "";

    /// <summary>체크된 총 개수와 그중 현재 검색에 가려진 개수. 일괄 생성은 검색과 무관하게
    /// 체크된 전부를 돌리므로(마스터 목록 기준), 화면에 안 보이는 체크가 있다는 사실이
    /// 사용자에게 보여야 한다 — 안 보이면 "왜 예상보다 많이 나왔지"가 된다.</summary>
    [ObservableProperty] private string _checkedCountText = "체크 없음";

    /// <summary>메인 화면에 보여 줄 훅 적용 요약. 훅은 별도 창에 있어서 켜 둔 걸 잊은 채
    /// 생성하면 "왜 태그가 더 붙지"가 된다 — 생성 패널에서 항상 보이게 한다.</summary>
    [ObservableProperty] private string _hookSummaryText = "훅 없음";

    /// <summary>프리셋 콤보에 띄울 목록. MainViewModel.BatchPresets를 이름순으로 비춘다.</summary>
    public ObservableCollection<BatchSelectionPreset> BatchPresets { get; } = new();
    [ObservableProperty] private BatchSelectionPreset? _selectedBatchPreset;
    /// <summary>"현재 선택 저장"에 쓸 이름 입력칸.</summary>
    [ObservableProperty] private string _batchPresetNameInput = "";

    [ObservableProperty] private int _lineCount = 100;
    /// <summary>"-1"은 "매번 랜덤"을 뜻하는 값(빈 문자열도 같은 뜻으로 계속 받아들임).
    /// 빈 칸으로 두면 "설정을 깜빡한 건가 랜덤인 건가" 헷갈릴 수 있어, 기본값 자체를
    /// -1로 명시해 화면에 보이게 했다.</summary>
    [ObservableProperty] private string _seedText = "-1";
    [ObservableProperty] private bool _dedupeWithinLine = true;
    [ObservableProperty] private bool _avoidDuplicateLines = true;
    [ObservableProperty] private bool _avoidConflicts = true;
    [ObservableProperty] private bool _insertBlankLine = true;
    [ObservableProperty] private bool _underscoreToSpace = true;
    /// <summary>빈도 가중 추첨(인기 태그가 더 자주). 기본 꺼짐 — 균등 추첨.</summary>
    [ObservableProperty] private bool _weightedSampling;
    /// <summary>표준 프롬프트 순서로 자동 정렬. 기본 꺼짐.</summary>
    [ObservableProperty] private bool _autoOrderTags;
    /// <summary>켜면 QualityTagsText를 매 줄 맨 앞에 붙인다. 자기만의 품질 태그 워크플로가
    /// 없는 사용자도 이 프로그램만으로 완성된 프롬프트를 바로 뽑을 수 있게 하는 옵트인 기능.</summary>
    [ObservableProperty] private bool _qualityTagsEnabled;
    [ObservableProperty] private string _qualityTagsText = "";
    [ObservableProperty] private WriteMode _mode = WriteMode.Append;
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _conflictReport = "";
    /// <summary>백그라운드 생성 진행률("1,200/22,700줄 (5%)"). 비어 있으면 진행 중이 아니다.</summary>
    [ObservableProperty] private string _progressText = "";
    /// <summary>방금 뽑은 줄들의 길이 요약(guide.md 줄 길이 예산 24~27태그 점검용).</summary>
    [ObservableProperty] private string _lineStatsText = "";
    /// <summary>일괄 생성 결과를 한 파일로 잇지 않고 레시피마다 따로 쓴다(출력 경로의 폴더에
    /// "레시피 이름.txt"). ComfyUI에서 컨셉별로 __와일드카드__를 부르려면 팩당 파일이 필요하다.</summary>
    [ObservableProperty] private bool _splitFilesPerRecipe;

    public WriteMode[] Modes { get; } = { WriteMode.New, WriteMode.Overwrite, WriteMode.Append };

    public GenerationViewModel(MainViewModel main)
    {
        _main = main;
        LoadSettings();
        foreach (var r in _main.SavedRecipes) _allBatchItems.Add(CreateBatchItem(r));
        RefreshBatchItems();
        RefreshBatchPresets();
        RefreshHookSummary();
    }

    partial void OnBatchSearchTextChanged(string value) => RefreshBatchItems();

    /// <summary>레시피 라이브러리가 바깥에서 바뀐 뒤(예: 번들 프리셋 갱신, 라벨 편집) 체크
    /// 목록을 다시 채운다. 이미 체크해 둔 레시피는 Id로 찾아 체크 상태를 그대로 옮긴다.</summary>
    public void RefreshBatchRecipes()
    {
        var checkedIds = _allBatchItems.Where(b => b.IsChecked).Select(b => b.Recipe.Id).ToHashSet();
        _allBatchItems.Clear();
        foreach (var r in _main.SavedRecipes)
        {
            var item = CreateBatchItem(r);
            if (checkedIds.Contains(r.Id)) item.IsChecked = true;
            _allBatchItems.Add(item);
        }
        RefreshBatchItems();
    }

    /// <summary>레시피 라이브러리 창에서 여러 레시피를 골라 "일괄 생성으로 보내기"를 눌렀을 때
    /// 호출된다. 기존 체크 상태는 이 선택으로 완전히 교체하고(라이브러리에서 방금 고른 것과
    /// 옛 체크가 섞여 나오는 걸 막음), 일괄 생성 모드도 함께 켠다.</summary>
    public void SetBatchSelection(IEnumerable<string> recipeIds)
    {
        var idSet = recipeIds.ToHashSet();
        foreach (var item in _allBatchItems) item.IsChecked = idSet.Contains(item.Recipe.Id);
        IsMultiRecipeMode = true;
        RefreshBatchItems();
    }

    /// <summary>검색어에 맞는 항목만 BatchItems에 다시 채운다. 이름뿐 아니라 SearchBlob(태그·
    /// 축 라벨·Labels)까지 훑으므로 "netorare" 같은 실제 태그명으로도 걸린다. _allBatchItems가
    /// 마스터라 체크 상태는 SelectableRecipeItem 인스턴스가 그대로 재사용되면서 유지된다.</summary>
    /// <summary>체크 상태가 바뀔 때마다 개수 표시를 갱신해야 해서, 항목 생성을 한 곳으로 모았다.</summary>
    private SelectableRecipeItem CreateBatchItem(Recipe recipe)
    {
        var item = new SelectableRecipeItem(recipe);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectableRecipeItem.IsChecked)) RefreshCheckedCount();
        };
        return item;
    }

    private void RefreshCheckedCount()
    {
        int total = _allBatchItems.Count(b => b.IsChecked);
        int visible = BatchItems.Count(b => b.IsChecked);
        int hidden = total - visible;
        CheckedCountText = total == 0
            ? "체크 없음"
            : hidden > 0 ? $"체크 {total}개 (검색에 가려진 {hidden}개 포함)" : $"체크 {total}개";
    }

    private void RefreshBatchItems()
    {
        BatchItems.Clear();
        var search = BatchSearchText.Trim();
        foreach (var item in _allBatchItems)
            if (string.IsNullOrWhiteSpace(search) || item.SearchBlob.Contains(search, StringComparison.OrdinalIgnoreCase))
                BatchItems.Add(item);
        // 가려진 개수는 검색어에 따라 달라지므로 목록을 다시 채울 때마다 계산한다.
        RefreshCheckedCount();
    }

    /// <summary>현재 검색으로 걸러진 목록만 일괄 체크/해제한다(검색으로 가려진 항목은 안 건드림).</summary>
    [RelayCommand]
    private void SelectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = true;
    }

    [RelayCommand]
    private void DeselectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = false;
    }

    /// <summary>NSFW(🔞)만 일괄 체크. 기존 체크는 유지하지 않고 이 그룹만 남긴다 —
    /// "NSFW 전체 돌려보기"가 주 용도라 매번 해제부터 누르지 않아도 되게.
    /// 검색 필터가 걸려 있으면 그 안에서만 동작한다(전체 선택 버튼과 같은 규칙).</summary>
    [RelayCommand]
    private void SelectNsfwBatchItems() => SelectByRating(nsfw: true);

    /// <summary>SFW(🔞 아님)만 일괄 체크.</summary>
    [RelayCommand]
    private void SelectSfwBatchItems() => SelectByRating(nsfw: false);

    private void SelectByRating(bool nsfw)
    {
        foreach (var item in BatchItems)
            item.IsChecked = !item.IsPreview && item.IsNsfw == nsfw;
    }

    /// <summary>훅 창에서 변경이 있을 때 HookLibraryViewModel이 호출한다.</summary>
    public void RefreshHookSummary()
    {
        int on = _main.Hooks.Count(h => h.IsEnabled);
        HookSummaryText = on == 0 ? "훅 없음" : $"🔗 훅 {on}개 적용 중";
    }

    private void RefreshBatchPresets()
    {
        var previous = SelectedBatchPreset;
        BatchPresets.Clear();
        foreach (var p in _main.BatchPresets.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            BatchPresets.Add(p);
        // 목록을 다시 채우면 콤보 선택이 풀린다 — 아직 살아 있는 프리셋이면 되돌려 준다.
        if (previous != null && BatchPresets.Contains(previous)) SelectedBatchPreset = previous;
    }

    /// <summary>프리셋의 레시피들을 체크한다. 저장 이후 사라진 레시피는 BatchSelectionResolver가
    /// 걸러 내며, 라이브러리 자체가 비정상으로 보이면 정리를 보류하고 경고만 남긴다.</summary>
    [RelayCommand]
    private void LoadBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "불러올 프리셋을 고르세요.";
            return;
        }

        var resolution = BatchSelectionResolver.Resolve(preset, _main.SavedRecipes);

        // 사라진 항목을 정리했거나 이름 폴백으로 id를 갱신했으면 저장한다.
        // 안전장치가 발동한 경우 ShouldSave는 false라 프리셋 파일을 건드리지 않는다.
        if (resolution.ShouldSave)
        {
            preset.Entries = resolution.Entries.ToList();
            preset.SavedAt = DateTime.Now;
            _main.SaveBatchPresets();
        }

        var idSet = resolution.MatchedRecipeIds.ToHashSet(StringComparer.Ordinal);
        foreach (var item in _allBatchItems) item.IsChecked = idSet.Contains(item.Recipe.Id);
        IsMultiRecipeMode = true;
        RefreshBatchItems();

        if (resolution.MissingNames.Count == 0)
        {
            _main.Status = $"프리셋 '{preset.Name}' 불러옴 — {resolution.MatchedRecipeIds.Count}개 선택됨.";
            return;
        }

        var names = string.Join(", ", resolution.MissingNames.Take(5))
                    + (resolution.MissingNames.Count > 5 ? $" 외 {resolution.MissingNames.Count - 5}개" : "");

        _main.Status = resolution.PruningWithheld
            // 라이브러리가 비었거나 절반 이상이 안 맞는다 — 지금 정리하면 프리셋이 사실상 사라진다.
            ? $"⚠ 프리셋 '{preset.Name}': {resolution.MissingNames.Count}개를 찾지 못했습니다({names}). "
              + "라이브러리가 정상인지 확인하세요 — 프리셋은 그대로 두었습니다."
            : $"프리셋 '{preset.Name}' 불러옴 — {resolution.MatchedRecipeIds.Count}개 선택됨. "
              + $"사라진 {resolution.MissingNames.Count}개 정리됨: {names}";
    }

    /// <summary>지금 체크된 레시피들을 프리셋 엔트리로 만든다. 검색으로 가려진 체크도 포함한다
    /// — 생성이 그 기준으로 도므로 저장도 같은 기준이어야 한다.</summary>
    private List<BatchSelectionEntry> CheckedEntries() => _allBatchItems
        .Where(b => b.IsChecked)
        .Select(b => new BatchSelectionEntry { RecipeId = b.Recipe.Id, RecipeName = b.Recipe.Name })
        .ToList();

    /// <summary>고른 프리셋을 지금 체크된 레시피들로 덮어쓴다(이름 유지).
    /// <para>"새로 저장"과 버튼을 나눈 이유: 하나로 합치면 이름칸을 고친 채 저장했을 때
    /// "이름 변경"인지 "새 프리셋"인지 의도가 갈린다. 버튼이 둘이면 섞일 여지가 없다.</para></summary>
    [RelayCommand]
    private void UpdateBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "업데이트할 프리셋을 고르세요.";
            return;
        }

        var entries = CheckedEntries();
        if (entries.Count == 0)
        {
            _main.Status = "체크된 레시피가 없습니다. 업데이트할 선택이 없습니다.";
            return;
        }

        // 덮어쓰면 이전 구성은 사라지므로 개수를 함께 보여 주고 확인받는다.
        var answer = System.Windows.MessageBox.Show(
            $"프리셋 '{preset.Name}'({preset.Entries.Count}개)을 지금 체크된 {entries.Count}개로 덮어쓸까요?",
            "프리셋 업데이트", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        preset.Entries = entries;
        preset.SavedAt = DateTime.Now;
        _main.SaveBatchPresets();
        _main.Status = $"프리셋 '{preset.Name}' 업데이트됨 — {entries.Count}개.";
    }

    /// <summary>이름칸의 이름으로 새 프리셋을 만든다. 같은 이름이 이미 있으면 확인 후 덮어쓴다.</summary>
    [RelayCommand]
    private void SaveBatchPreset()
    {
        var name = BatchPresetNameInput.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _main.Status = "프리셋 이름을 입력하세요.";
            return;
        }

        var entries = CheckedEntries();
        if (entries.Count == 0)
        {
            _main.Status = "체크된 레시피가 없습니다. 저장할 선택이 없습니다.";
            return;
        }

        var existing = _main.BatchPresets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            var answer = System.Windows.MessageBox.Show(
                $"'{name}' 프리셋이 이미 있습니다. 덮어쓸까요?", "프리셋 덮어쓰기",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            existing.Entries = entries;
            existing.SavedAt = DateTime.Now;
        }
        else
        {
            _main.BatchPresets.Add(new BatchSelectionPreset { Name = name, Entries = entries });
        }

        _main.SaveBatchPresets();
        RefreshBatchPresets();
        SelectedBatchPreset = BatchPresets.FirstOrDefault(p => p.Name == name);
        BatchPresetNameInput = "";
        _main.Status = $"프리셋 '{name}' 저장됨 — {entries.Count}개.";
    }

    [RelayCommand]
    private void DeleteBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "삭제할 프리셋을 고르세요.";
            return;
        }
        var answer = System.Windows.MessageBox.Show(
            $"프리셋 '{preset.Name}'을 삭제할까요?", "프리셋 삭제",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _main.BatchPresets.Remove(preset);
        _main.SaveBatchPresets();
        SelectedBatchPreset = null;
        RefreshBatchPresets();
        _main.Status = $"프리셋 '{preset.Name}' 삭제됨.";
    }

    /// <summary>재시작해도 생성 탭 설정(출력 경로, 줄 수, 체크박스들)이 남아있도록 저장해둔 값을 복원.
    /// 이전엔 Settings에 저장하는 코드 자체가 없어 재시작하면 항상 기본값으로 돌아갔다.</summary>
    private void LoadSettings()
    {
        var s = _main.Settings;
        OutputPath = s.LastOutputPath;
        LineCount = s.DefaultLineCount;
        Mode = s.LastMode;
        DedupeWithinLine = s.DedupeWithinLine;
        AvoidDuplicateLines = s.AvoidDuplicateLines;
        AvoidConflicts = s.AvoidConflicts;
        InsertBlankLine = s.InsertBlankLine;
        UnderscoreToSpace = s.UnderscoreToSpace;
        WeightedSampling = s.WeightedSampling;
        AutoOrderTags = s.AutoOrderTags;
        QualityTagsEnabled = s.QualityTagsEnabled;
        QualityTagsText = s.QualityTagsText;
        SplitFilesPerRecipe = s.SplitFilesPerRecipe;
    }

    /// <summary>현재 생성 탭 설정을 Settings에 담아 디스크에 저장한다.
    /// 생성 성공 직후와 앱 종료 시(MainWindow의 Closing) 둘 다에서 호출된다.</summary>
    public void SaveSettings()
    {
        var s = _main.Settings;
        s.LastOutputPath = OutputPath;
        s.DefaultLineCount = LineCount;
        s.LastMode = Mode;
        s.DedupeWithinLine = DedupeWithinLine;
        s.AvoidDuplicateLines = AvoidDuplicateLines;
        s.AvoidConflicts = AvoidConflicts;
        s.InsertBlankLine = InsertBlankLine;
        s.UnderscoreToSpace = UnderscoreToSpace;
        s.WeightedSampling = WeightedSampling;
        s.AutoOrderTags = AutoOrderTags;
        s.QualityTagsEnabled = QualityTagsEnabled;
        s.QualityTagsText = QualityTagsText;
        s.SplitFilesPerRecipe = SplitFilesPerRecipe;
        SettingsStore.Save(s);
    }

    private GenerationOptions BuildOptions() => new()
    {
        LineCount = LineCount,
        // -1이나 빈 칸(파싱 실패)이면 null → Run()에서 매번 새 랜덤 시드를 뽑는다.
        Seed = int.TryParse(SeedText, out var s) && s != -1 ? s : null,
        DedupeWithinLine = DedupeWithinLine,
        AvoidDuplicateLines = AvoidDuplicateLines,
        AvoidConflicts = AvoidConflicts,
        UnderscoreToSpace = UnderscoreToSpace,
        Sampling = WeightedSampling ? SamplingMode.Weighted : SamplingMode.Uniform,
        AutoOrderTags = AutoOrderTags,
    };

    /// <summary>이번 호출에 실제로 쓰인 시드. 자동(랜덤) 시드일 때도 결과에 남겨 사용자가
    /// "정말 매번 다른 시드로 도는지" 눈으로 확인할 수 있게 한다.</summary>
    private int _lastSeedUsed;

    /// <summary>백그라운드로 넘길 작업 스냅샷. 슬롯·풀은 ObservableCollection이라 생성이
    /// 도는 동안 빌더나 라이브러리 창에서 태그를 고치면 백그라운드 읽기와 겹쳐 깨진다 —
    /// JSON 왕복으로 깊은 복사를 떠서 넘긴다(체크한 레시피만 복사하므로 비용이 제한적).</summary>
    private sealed record GenerationJob(
        IReadOnlyList<Recipe> Recipes,
        IReadOnlyDictionary<string, Pool> Pools,
        GenerationOptions Options,
        int LineCountEach,
        int Seed);

    private static Recipe Clone(Recipe recipe) =>
        System.Text.Json.JsonSerializer.Deserialize<Recipe>(
            System.Text.Json.JsonSerializer.Serialize(recipe, JsonStore.Options), JsonStore.Options)!;

    /// <summary>UI 스레드에서만 할 수 있는 일(슬롯·체크 상태·풀 읽기, 시드 확정)을 먼저 끝내
    /// 백그라운드가 건드릴 게 없는 스냅샷으로 만든다.</summary>
    /// <summary>후보가 없어 건너뛴 훅의 이름들. 생성이 끝난 뒤 상태 메시지로 알린다 —
    /// 조용히 빠지면 "훅을 켰는데 왜 안 나오지"가 된다.</summary>
    private IReadOnlyList<string> _skippedHooks = Array.Empty<string>();

    private GenerationJob BuildJob(int lineCountEach)
    {
        var opts = BuildOptions();
        // 시드를 비워뒀으면(자동) 여기서 직접 하나 뽑아 못박는다 — SystemRandomSource에 null을
        // 넘기면 내부에서 알아서 뽑긴 하지만 그 값을 밖에서 확인할 방법이 없어진다.
        _lastSeedUsed = opts.Seed ?? Random.Shared.Next();
        var recipes = IsMultiRecipeMode
            // 마스터 목록을 읽는다. BatchItems는 검색어로 걸러진 표시용이라, 레시피를 체크해 둔 뒤
            // 검색창에 무언가를 입력하면 가려진 항목이 조용히 생성에서 빠졌다(성공으로 끝나므로
            // 눈치채기도 어려웠다). 검색은 "무엇을 보여줄지"만 정해야지 "무엇을 생성할지"를
            // 정해서는 안 된다.
            ? _allBatchItems.Where(b => b.IsChecked).Select(b => Clone(b.Recipe)).ToList()
            : new List<Recipe> { Clone(_main.RecipeBuilder.BuildRecipe()) };

        var pools = _main.Pools.ToDictionary(p => p.Id);

        // 훅은 복사본에만 끼운다 — 원본 레시피는 절대 건드리지 않는다. 일괄·단일 두 경로가
        // 모두 여기를 지나므로 훅은 자동으로 양쪽(미리보기 포함)에 적용된다.
        var hooks = _main.Hooks.Where(h => h.IsEnabled).OrderBy(h => h.Order).ToList();
        var skipped = new List<string>();
        foreach (var recipe in recipes)
            foreach (var name in HookApplier.Apply(recipe, hooks, pools))
                if (!skipped.Contains(name)) skipped.Add(name);
        _skippedHooks = skipped;

        return new GenerationJob(recipes, pools, opts, lineCountEach, _lastSeedUsed);
    }

    /// <summary>후보가 없어 건너뛴 훅이 있으면 알린다.</summary>
    private string SkippedHookNote() =>
        _skippedHooks.Count == 0 ? "" : $" ⚠ 후보가 없어 건너뛴 훅: {string.Join(", ", _skippedHooks)}";

    /// <summary>레시피 하나의 생성 결과(어느 레시피가 뽑았는지 함께). 레시피별 파일 분리
    /// 출력이 "어느 파일에 무엇을 쓸지" 알려면 합치기 전 단위가 남아 있어야 한다.</summary>
    private sealed record RecipeOutput(Recipe Recipe, GenerationResult Result);

    /// <summary>레시피마다 lineCountEach줄씩 뽑는다. 레시피마다 시드를 base+순번으로 달리해
    /// 서로 다른 레시피가 완전히 같은 난수 시퀀스를 타지 않게 한다. UI를 전혀 건드리지
    /// 않으므로 백그라운드 스레드에서 그대로 돈다.</summary>
    private List<RecipeOutput> RunJob(GenerationJob job, IProgress<int> progress, CancellationToken token)
    {
        var outputs = new List<RecipeOutput>(job.Recipes.Count);
        int done = 0;

        for (int i = 0; i < job.Recipes.Count; i++)
        {
            var opts = CopyWith(job.Options, job.LineCountEach, unchecked(job.Seed + i));
            int completedBefore = done;
            var inner = new Progress<int>(n => progress.Report(completedBefore + n));

            var result = _generator.Generate(job.Recipes[i], job.Pools, opts, _main.Conflicts, _main.TagInfo, inner, token);

            outputs.Add(new RecipeOutput(job.Recipes[i], result));
            done += result.Lines.Count;
        }

        return outputs;
    }

    /// <summary>레시피별 결과를 한 덩어리로 잇는다(레시피별로 묶여 순서대로 — 랜덤 셔플 아님).
    /// 모순 줄 인덱스는 이어붙인 뒤의 전체 위치로 보정하고, 경고에는 어느 레시피인지 붙인다.</summary>
    private static GenerationResult Merge(IReadOnlyList<RecipeOutput> outputs)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var conflicts = new List<LineConflict>();

        foreach (var o in outputs)
        {
            int offset = lines.Count;
            foreach (var c in o.Result.Conflicts) conflicts.Add(c with { LineIndex = c.LineIndex + offset });
            lines.AddRange(o.Result.Lines);
            foreach (var w in o.Result.Warnings)
                warnings.Add(outputs.Count > 1 ? $"[{o.Recipe.Name}] {w}" : w);
        }

        return new GenerationResult(lines, warnings) { Conflicts = conflicts };
    }

    /// <summary>줄 수와 시드만 바꾼 사본. 백그라운드에서 뷰모델 속성을 다시 읽지 않으려고
    /// 스냅샷의 값만으로 만든다.</summary>
    private static GenerationOptions CopyWith(GenerationOptions o, int lineCount, int seed) => new()
    {
        LineCount = lineCount,
        Seed = seed,
        DedupeWithinLine = o.DedupeWithinLine,
        AvoidDuplicateLines = o.AvoidDuplicateLines,
        AvoidConflicts = o.AvoidConflicts,
        UnderscoreToSpace = o.UnderscoreToSpace,
        Sampling = o.Sampling,
        AutoOrderTags = o.AutoOrderTags,
        Blocklist = o.Blocklist,
    };

    /// <summary>스냅샷을 백그라운드에서 돌리고 진행률을 UI로 보고한다. 예전엔 생성이 UI
    /// 스레드에서 통째로 돌아, 레시피 수십 개 × 수백 줄이면 창이 몇 초씩 응답하지 않았다.</summary>
    private async Task<List<RecipeOutput>> RunInBackgroundAsync(GenerationJob job, CancellationToken token)
    {
        long total = (long)job.Recipes.Count * job.LineCountEach;
        // Progress<T>는 만들어진 스레드(여기선 UI)의 컨텍스트로 콜백을 돌려준다.
        var progress = new Progress<int>(n =>
            ProgressText = total > 0 ? $"{n:N0}/{total:N0}줄 ({n * 100 / total}%)" : "");
        return await Task.Run(() => RunJob(job, progress, token), token);
    }

    /// <summary>QualityTagsEnabled가 켜져 있으면 QualityTagsText를 매 줄 맨 앞에 붙인다.
    /// 줄 수·모순 인덱스는 그대로 두고 텍스트만 바꾸므로 Conflicts의 LineIndex는 영향받지 않는다.</summary>
    private GenerationResult ApplyQualityTags(GenerationResult result)
    {
        if (!QualityTagsEnabled || string.IsNullOrWhiteSpace(QualityTagsText)) return result;
        var prefix = QualityTagsText.Trim();
        var prefixed = result.Lines.Select(l => string.IsNullOrEmpty(l) ? l : $"{prefix}, {l}").ToList();
        return result with { Lines = prefixed };
    }

    /// <summary>줄 길이 예산(guide.md 24~27태그) 요약을 갱신한다. 예산을 넘는 줄이 있으면
    /// 몇 줄인지 같이 알려 준다 — 넘으면 뒤쪽 태그가 밀려 반영이 약해진다.</summary>
    private void RefreshLineStats(GenerationResult result)
    {
        var stats = LineStats.Measure(result.Lines);
        if (stats.LineCount == 0) { LineStatsText = ""; return; }
        LineStatsText = $"줄당 태그 평균 {stats.Average} · 최소 {stats.Min} · 최대 {stats.Max}"
            + (stats.OverBudget > 0 ? $"  ⚠ 예산({stats.Budget}) 초과 {stats.OverBudget}줄" : "");
    }

    /// <summary>미리보기 내용을 클립보드로. 읽기 전용 상자에서 드래그로 긁는 것보다 빠르다.</summary>
    [RelayCommand]
    private void CopyPreview()
    {
        if (string.IsNullOrEmpty(PreviewText)) { _main.Status = "복사할 미리보기 내용이 없습니다."; return; }
        try
        {
            System.Windows.Clipboard.SetText(PreviewText);
            _main.Status = $"미리보기 {PreviewText.Split('\n').Length}줄을 클립보드에 복사했습니다.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 다른 프로그램이 클립보드를 붙들고 있으면 실패한다 — 흔한 일이라 죽지 않게 한다.
            _main.Status = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다. 잠시 후 다시 시도하세요.";
        }
    }

    /// <summary>결과의 모순 줄들을 사람이 읽을 요약으로. 없으면 빈 문자열.</summary>
    private static string BuildConflictReport(GenerationResult result)
    {
        if (result.Conflicts.Count == 0) return "";
        var lines = result.Conflicts.Take(20).Select(c =>
            $"· {c.LineIndex + 1}번째 줄: " +
            string.Join("; ", c.Hits.Select(h => $"{h.Label}({string.Join(" + ", h.Tags)})")));
        var more = result.Conflicts.Count > 20 ? $"\n…외 {result.Conflicts.Count - 20}줄 더" : "";
        return $"⚠ 모순 {result.Conflicts.Count}줄:\n" + string.Join("\n", lines) + more;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "와일드카드 저장 위치",
            Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = string.IsNullOrWhiteSpace(OutputPath) ? "wildcard.txt" : System.IO.Path.GetFileName(OutputPath),
            OverwritePrompt = false, // append 모드 지원 위해 덮어쓰기 경고 끔
        };
        if (!string.IsNullOrWhiteSpace(_main.Settings.LastOutputDir) && System.IO.Directory.Exists(_main.Settings.LastOutputDir))
            dlg.InitialDirectory = _main.Settings.LastOutputDir;
        if (dlg.ShowDialog() == true) OutputPath = dlg.FileName;
    }

    /// <summary>여러 레시피 모드인데 체크된 게 하나도 없으면 상태 메시지만 남기고 취소.
    /// true를 반환하면(막혔으면) 호출부는 더 진행하지 않는다.</summary>
    private bool MultiModeBlockedWithNoSelection()
    {
        // BuildJob과 같은 목록을 봐야 한다 — 표시용을 보면 "검색에 가려졌을 뿐 체크는 돼 있는"
        // 상태에서 생성이 막혀 버린다.
        if (!IsMultiRecipeMode || _allBatchItems.Any(b => b.IsChecked)) return false;
        _main.Status = "일괄 생성할 레시피를 하나 이상 체크하세요.";
        return true;
    }

    /// <summary>돌고 있는 쪽(미리보기든 생성이든)을 중단한다. 둘 다 같은 진행률 표시를 쓰므로
    /// 버튼도 하나로 합쳤다.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (PreviewCommand.CanBeCanceled) PreviewCommand.Cancel();
        if (GenerateCommand.CanBeCanceled) GenerateCommand.Cancel();
    }

    [RelayCommand]
    private async Task Preview(CancellationToken token)
    {
        if (MultiModeBlockedWithNoSelection()) return;
        try
        {
            var result = ApplyQualityTags(Merge(await RunInBackgroundAsync(BuildJob(Math.Min(20, LineCount)), token)));
            PreviewText = string.Join("\n", result.Lines);
            ConflictReport = BuildConflictReport(result);
            RefreshLineStats(result);
            var seedNote = $"(시드 {_lastSeedUsed})";
            _main.Status = (result.Warnings.Count > 0 ? string.Join(" / ", result.Warnings) : "미리보기 완료")
                + " " + seedNote + SkippedHookNote();
        }
        catch (OperationCanceledException) { _main.Status = "미리보기 취소됨"; }
        catch (GenerationValidationException ex) { _main.Status = "검증 오류: " + ex.Message; }
        finally { ProgressText = ""; }
    }

    [RelayCommand]
    private async Task Generate(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(OutputPath)) { _main.Status = "출력 경로를 지정하세요."; return; }
        if (MultiModeBlockedWithNoSelection()) return;
        try
        {
            var outputs = await RunInBackgroundAsync(BuildJob(LineCount), token);
            var result = ApplyQualityTags(Merge(outputs));

            // 파일 쓰기도 백그라운드로. 취소된 뒤엔 아예 쓰지 않는다(파일은 그대로 남는다).
            var (path, mode, blank) = (OutputPath, Mode, InsertBlankLine);
            string where;
            if (SplitFilesPerRecipe && IsMultiRecipeMode)
            {
                var folder = Path.GetDirectoryName(path) ?? "";
                var names = WildcardWriter.ToFileNames(outputs.Select(o => (o.Recipe.Name, o.Recipe.Id)));
                var files = outputs.Select((o, i) =>
                    (Path: Path.Combine(folder, names[i] + ".txt"),
                     Lines: ApplyQualityTags(o.Result).Lines)).ToList();
                await Task.Run(() =>
                {
                    foreach (var f in files)
                    {
                        token.ThrowIfCancellationRequested();
                        WildcardWriter.Write(f.Path, f.Lines, mode, blank);
                    }
                }, token);
                where = $"{files.Count}개 파일 → {folder}";
            }
            else
            {
                await Task.Run(() => WildcardWriter.Write(path, result.Lines, mode, blank), token);
                where = path;
            }

            _main.Settings.LastOutputDir = Path.GetDirectoryName(OutputPath) ?? "";
            SaveSettings();
            ConflictReport = BuildConflictReport(result);
            RefreshLineStats(result);
            _main.Status = $"{result.Lines.Count}줄 {Mode} 완료 → {where} (시드 {_lastSeedUsed})"
                + (result.Warnings.Count > 0 ? " (" + string.Join(", ", result.Warnings) + ")" : "")
                + SkippedHookNote();
        }
        catch (OperationCanceledException) { _main.Status = "생성 취소됨 — 파일은 건드리지 않았습니다."; }
        catch (GenerationValidationException ex) { _main.Status = "검증 오류: " + ex.Message; }
        catch (IOException ex) { _main.Status = "파일 오류: " + ex.Message; }
        finally { ProgressText = ""; }
    }
}
