using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using DanbooruTagGen.Core.Tags;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>미리보기 한 슬롯의 편집 가능한 뷰. InlineTags는 라이브러리에 저장된 슬롯의
/// Tags 컬렉션 그 자체를 바인딩하므로(복사본 아님), 여기서 지우거나 추가하면 레시피가
/// 바로 수정되고 창을 닫을 때 자동 저장(Persist)으로 확정된다. 풀(PoolId) 참조 후보는
/// 공용 풀을 건드리게 되므로 여기선 읽기 전용으로만 보여준다 — 풀 수정은 풀 라이브러리에서.</summary>
public sealed partial class SlotPreviewViewModel : ObservableObject
{
    private readonly Slot _slot;
    /// <summary>이 슬롯이 참조하는 풀(들) — PoolId 하나 + 체이닝된 ExtraPoolIds 전부.</summary>
    private readonly IReadOnlyList<Pool> _pools;
    /// <summary>태그 추가 자동완성용 조회. 앱 시작 전(TagDb 로드 전)이면 null — 자동완성만
    /// 조용히 비활성화되고 직접 타이핑은 그대로 동작한다.</summary>
    private readonly TagDatabase? _tagDb;
    /// <summary>태그 칩의 입력 즉시 검사(사전에 없는 태그 빨갛게)용 조회.</summary>
    public Core.Generation.ITagLookup? TagInfo => _tagDb;

    /// <summary>이 미리보기에서 태그를 고칠 때마다 불린다. 번들 팩을 앱 안에서 고치면 다음
    /// 프리셋 갱신 때 덮어써지므로, 그 사실을 고치는 순간 알려 주려고 라이브러리가 건다.</summary>
    private readonly Action? _onEdited;

    public SlotPreviewViewModel(Slot slot, IReadOnlyList<Pool> pools, TagDatabase? tagDb = null, Action? onEdited = null)
    {
        _slot = slot;
        _pools = pools;
        _tagDb = tagDb;
        _onEdited = onEdited;
        InlineTags = slot switch
        {
            FixedSlot f => f.Tags,
            RandomPoolSlot r => r.Tags,
            _ => new System.Collections.ObjectModel.ObservableCollection<string>(),
        };
        RefreshHeader();
    }

    /// <summary>부모(RecipeLibraryViewModel)가 순서 이동·삭제 때 Recipe.Slots에서 이 항목을
    /// 찾는 데 쓴다 — 미리보기 항목은 늘 Slot과 1:1이므로 참조 동일성으로 찾으면 된다.</summary>
    public Slot UnderlyingSlot => _slot;

    /// <summary>슬롯의 실제 태그 컬렉션(수정 즉시 반영).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> InlineTags { get; }
    public bool HasPool => _pools.Count > 0;
    public string PoolTagsText => _pools.Count == 0 ? "" : string.Join("\n",
        _pools.Select(p => $"풀 '{p.Name}' 후보(수정은 풀 라이브러리에서): {string.Join(", ", p.Candidates)}"));

    /// <summary>랜덤 슬롯일 때만 min/max 편집칸을 보여준다(고정/대안 슬롯엔 개수 개념이 없음).</summary>
    public bool IsRandomPool => _slot is RandomPoolSlot;

    /// <summary>슬롯의 MinCount/MaxCount를 직접 편집한다(레시피 빌더로 불러올 필요 없이
    /// 라이브러리에서 바로). 레시피 빌더(RecipeBuilderView.xaml)의
    /// "TextBox Text={Binding MinCount}" 패턴과 동일하게 슬롯 속성에 바로 바인딩.</summary>
    public int MinCount
    {
        get => (_slot as RandomPoolSlot)?.MinCount ?? 0;
        set { if (_slot is RandomPoolSlot r && r.MinCount != value) { r.MinCount = value; OnPropertyChanged(); RefreshHeader(); } }
    }

    public int MaxCount
    {
        get => (_slot as RandomPoolSlot)?.MaxCount ?? 0;
        set { if (_slot is RandomPoolSlot r && r.MaxCount != value) { r.MaxCount = value; OnPropertyChanged(); RefreshHeader(); } }
    }

    /// <summary>발동 확률 편집칸(비우면 100). 파싱·오류 문구는 <see cref="RandomPoolSlot.ChancePercentText"/>가
    /// 맡아 빌더와 같은 규칙을 쓴다.</summary>
    public string ChancePercentText
    {
        get => (_slot as RandomPoolSlot)?.ChancePercentText ?? "";
        set
        {
            if (_slot is not RandomPoolSlot r) return;
            r.ChancePercentText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChancePercentError));
            OnPropertyChanged(nameof(HasChancePercentError));
            RefreshHeader();
        }
    }

    public string ChancePercentError => (_slot as RandomPoolSlot)?.ChancePercentError ?? "";
    public bool HasChancePercentError => ChancePercentError.Length > 0;

    [ObservableProperty] private string _header = "";
    [ObservableProperty] private string _addTagText = "";

    /// <summary>가중치 범위 표기((tag:1.1~1.3))가 잘못돼 추가하지 않은 이유. 비어 있으면 숨긴다.
    /// 여기서 안 막으면 저장은 되고 생성할 때에야 검증 오류로 터져, 어디서 틀렸는지 찾기 어렵다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddTagError))]
    private string _addTagError = "";
    public bool HasAddTagError => AddTagError.Length > 0;

    /// <summary>추가 입력칸의 자동완성 후보(최대 8개). 콤마로 여러 개를 입력하는 중이면
    /// 마지막 조각만 검색어로 쓴다 — "1girl, sm" 이라고 치는 중이면 "sm"만 검색.</summary>
    public ObservableCollection<Tag> Suggestions { get; } = new();
    public bool HasSuggestions => Suggestions.Count > 0;

    partial void OnAddTagTextChanged(string value)
    {
        AddTagError = "";
        Suggestions.Clear();
        // 괄호 밖 쉼표만 항목 구분 — 안 닫힌 "(she is dragged in, sti"를 치는 중이면 전체가 한 항목이라 "sti"로
        // 검색하지 않는다(거기서 후보를 누르면 문장 중간이 태그로 바뀌어 버린다).
        var lastPiece = value[(Core.Generation.TagKey.LastTopLevelComma(value) + 1)..].Trim();
        if (_tagDb != null && lastPiece.Length > 0)
            foreach (var t in _tagDb.SearchRanked(lastPiece, 8)) Suggestions.Add(t);
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>자동완성 목록에서 하나를 클릭하면 입력칸의 마지막 조각을 그 태그로 바꿔
    /// 즉시 슬롯에 추가한다(오타 방지가 목적이므로 클릭 한 번으로 끝나야 함).</summary>
    [RelayCommand]
    private void SelectSuggestion(Tag tag)
    {
        var comma = Core.Generation.TagKey.LastTopLevelComma(AddTagText);
        AddTagText = comma >= 0 ? AddTagText[..(comma + 1)] + " " + tag.Name : tag.Name;
        AddTag();
    }

    private void RefreshHeader()
    {
        string offMark = _slot.IsEnabled ? "" : " (꺼짐)";
        int poolCandidateCount = _pools.Sum(p => p.Candidates.Count);
        string poolNote = _pools.Count switch
        {
            0 => "",
            1 => $", 풀: {_pools[0].Name}",
            _ => $", 풀 {_pools.Count}개 체이닝: {string.Join(" + ", _pools.Select(p => p.Name))}",
        };
        Header = _slot switch
        {
            FixedSlot f => $"[고정] {f.Label} — {f.Tags.Count}개{offMark}",
            RandomPoolSlot r => $"[랜덤] {r.Label} — " + (r.ChancePercent is >= 0 and < 100 ? $"{r.ChancePercent}% 확률로 " : "") +
                                $"매줄 {r.MinCount}~{r.MaxCount}개 " +
                                $"(후보 {r.Tags.Count + poolCandidateCount}개{poolNote}){offMark}",
            AlternativeSlot alt => $"[대안] {alt.Label} — 그룹 {alt.Groups.Count}개 중 매줄 하나만 통째로 " +
                                   $"({string.Join(" / ", alt.Groups.Select(g => $"{g.Label}:{g.Tags.Count}개"))}){offMark}",
            _ => _slot.Label,
        };
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        InlineTags.Remove(tag);
        RefreshHeader();
        _onEdited?.Invoke();
    }

    [RelayCommand]
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(AddTagText)) return;
        if (RecipeBuilderViewModel.UnbalancedInputError(AddTagText) is { } unbalanced) { AddTagError = unbalanced; return; }
        var rejected = new List<string>();
        string error = "";
        foreach (var piece in Core.Generation.TagKey.SplitList(AddTagText))
        {
            if (Core.Generation.TagKey.FindInputError(piece) is { } err)
            {
                rejected.Add(piece);
                if (error.Length == 0) error = $"'{piece}' 추가 안 됨: {err}";
                continue;
            }
            if (!InlineTags.Contains(piece)) InlineTags.Add(piece);
        }
        // 잘못된 조각만 입력칸에 남겨 바로 고칠 수 있게 한다(AddTagText 대입이 오류를 지우므로 나중에 설정).
        AddTagText = string.Join(", ", rejected);
        AddTagError = error;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
        RefreshHeader();
        _onEdited?.Invoke();
    }
}

/// <summary>전체 검사 결과 한 줄 — 손볼 것이 있는 팩 하나와 점검 리포트(🩺 점검·tools/RecipeCheck와 같은 기준).</summary>
public sealed record AuditRow(string RecipeId, string RecipeName, int MustFix, string Detail)
{
    public string Header => $"{RecipeName} — 손볼 것 {MustFix}건";
}

/// <summary>레시피(슬롯 구성 전체)를 이름 붙여 여러 개 저장/불러오기. Pool 라이브러리는 태그
/// 묶음을 여러 개 저장해 놓고 골라 쓸 수 있는데, 레시피 빌더는 "마지막 작업 상태" 하나만
/// 자동 저장/복원했었다 — 레시피 전체 구성도 여러 프리셋으로 저장해 두고 바꿔 쓰고 싶다는
/// 요청으로 추가.</summary>
public sealed partial class RecipeLibraryViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private Recipe? _selectedRecipe;
    /// <summary>Ctrl/Shift로 고른 다중 선택(코드비하인드가 ListBox.SelectedItems에서 옮겨온다).
    /// SelectedRecipe는 그대로 "마지막 포커스 항목" 하나만 가리켜 기존 미리보기·불러오기 동작을
    /// 그대로 유지한다 — 이건 "일괄 생성으로 보내기"에서만 쓰는 별도 상태.</summary>
    private readonly List<Recipe> _multiSelected = new();
    /// <summary>전체 목록(마스터). 저장/삭제 등 모든 조작은 여기에 하고,
    /// 화면 목록은 필터를 거친 FilteredRecipes가 담당한다.</summary>
    public ObservableCollection<Recipe> Recipes { get; }
    /// <summary>필터(전체/일반/성인)를 통과한 표시용 목록. 팩이 수십 개로 늘면서
    /// 태그 검색과 같은 3버튼 필터를 라이브러리에도 달았다.</summary>
    public ObservableCollection<Recipe> FilteredRecipes { get; } = new();
    [ObservableProperty] private NsfwFilterMode _recipeFilter = NsfwFilterMode.All;
    /// <summary>이름 검색(부분 일치, 대소문자 무시). 팩이 100개를 넘어서면서 스크롤만으로는
    /// 찾기 어려워져 추가 — 성인 필터와 AND로 결합된다.</summary>
    [ObservableProperty] private string _searchText = "";

    /// <summary>카테고리 드롭다운 후보("(전체)" 포함). Recipes가 바뀔 때마다 다시 모은다.</summary>
    public ObservableCollection<string> Categories { get; } = new();
    public const string AllCategoriesLabel = "(전체)";
    /// <summary>선택된 카테고리(AllCategoriesLabel=필터 없음). 성인/검색과 AND로 결합.</summary>
    [ObservableProperty] private string _selectedCategory = AllCategoriesLabel;

    /// <summary>"⭐ 즐겨찾기만" 체크 — 성인/검색/카테고리와 AND로 결합.</summary>
    [ObservableProperty] private bool _showFavoritesOnly;
    /// <summary>"🆕 새로·바뀜만" 체크 — 프리셋 갱신으로 들어왔거나 바뀐 뒤 아직 안 열어 본 레시피만.</summary>
    [ObservableProperty] private bool _showChangedOnly;

    public const string SortDefault = "기본 순서";
    public const string SortRecentlyAdded = "최근 추가순";
    public const string SortRecentlyChanged = "최근 수정순";
    public const string SortRecentlyUsed = "최근 사용순";
    public const string SortByName = "이름순";
    public string[] SortModes { get; } = { SortDefault, SortRecentlyAdded, SortRecentlyChanged, SortRecentlyUsed, SortByName };
    /// <summary>목록 정렬. "기본 순서"는 예전 그대로(일반 → 성인, recipes.json 순서). "최근 추가순"은 그 역순(새 프리셋은 맨 뒤에 붙는다).</summary>
    [ObservableProperty] private string _sortMode = SortDefault;
    /// <summary>분류(Category)별로 접었다 펼 수 있게 묶어 보기.</summary>
    [ObservableProperty] private bool _groupByCategory;
    /// <summary>"🆕 3 · ✏️ 1" — 아직 안 열어 본 새/바뀐 레시피 수. 없으면 빈 문자열(체크박스·버튼도 숨김).</summary>
    [ObservableProperty] private string _changedCountText = "";
    /// <summary>선택 레시피가 즐겨찾기인지(버튼 라벨/상태 바인딩용). SelectedRecipe나
    /// 즐겨찾기 목록이 바뀔 때마다 갱신.</summary>
    [ObservableProperty] private bool _selectedRecipeIsFavorite;

    /// <summary>라이브러리 창을 안 거치고 바로 뽑아보는 짧은 샘플(5줄). 빌더 상태는 건드리지 않는다.</summary>
    [ObservableProperty] private string _quickPreviewText = "";

    /// <summary>선택 레시피의 검증 지적("없는 태그"·"상대 없는 행위"). 비어 있으면 문제 없음.</summary>
    [ObservableProperty] private string _validationText = "";

    /// <summary>고른 레시피의 고치기 전 내용(직렬화). 첫 편집 때 기준 버전으로 남기고 비운다.</summary>
    private (string Id, string Json)? _baseline;

    /// <summary>선택 레시피 점검 리포트(검증·모순·조합·길이·명목 대비 실측) — tools/RecipeCheck와 같은 Core 리포트.</summary>
    [RelayCommand]
    private async Task CheckSelected()
    {
        if (SelectedRecipe is not { } selected) { _main.Status = "점검할 레시피를 고르세요."; return; }
        var copy = RecipeMeasure.Clone(selected);
        var pools = _main.Pools.ToDictionary(p => p.Id);
        var opts = _main.Generation.SnapshotOptions();
        var (rules, tags, requires) = (_main.Conflicts, _main.TagInfo, _main.Requires);
        var hooks = _main.Hooks.ToList();
        _main.Status = $"'{selected.Name}' 점검 중…";
        try
        {
            var il = Core.Tokenization.TokenCounters.For(Core.Tokenization.ModelKind.IL, AppPaths.TokenizersDir);
            var anima = Core.Tokenization.TokenCounters.For(Core.Tokenization.ModelKind.Anima, AppPaths.TokenizersDir);
            var (_, text) = await Task.Run(() => Core.Generation.RecipeCheckReport.Build(copy, pools, opts, rules, tags, requires, hooks, Suggest,
                il: il, anima: anima));
            var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                        ?? System.Windows.Application.Current.MainWindow;
            new Views.ReportWindow($"점검 — {selected.Name}", text) { Owner = owner }.Show();
            _main.Status = "점검 완료 — " + text.Split('\n')[^1];
        }
        catch (Core.Generation.GenerationValidationException ex) { _main.Status = "점검할 수 없음 — 검증 오류: " + ex.Message; }
        catch (InvalidOperationException ex) { _main.Status = "점검 중 오류(다시 시도하세요): " + ex.Message; }
    }

    /// <summary>선택 레시피 요약(마크다운: 슬롯·브랜치·후보 + 실측 지표)을 클립보드로 — 다른 작업 세션에 그대로 넘기는 용도.</summary>
    [RelayCommand]
    private async Task CopySummary()
    {
        if (SelectedRecipe is not { } selected) { _main.Status = "요약할 레시피를 고르세요."; return; }
        var copy = RecipeMeasure.Clone(selected);
        var pools = _main.Pools.ToDictionary(p => p.Id);
        var opts = _main.Generation.SnapshotOptions();
        var (rules, tags) = (_main.Conflicts, _main.TagInfo);
        _main.Status = "요약 만드는 중…";
        string md;
        try
        {
            md = await Task.Run(() => Core.Output.RecipeMarkdown.Export(copy, pools,
                Core.Generation.RecipeProfiler.Measure(copy, pools, opts, rules, tags, RecipeMeasure.SampleLines)));
        }
        catch (Core.Generation.GenerationValidationException)
        {
            md = Core.Output.RecipeMarkdown.Export(copy, pools);   // 생성이 안 되는 레시피도 구조는 보여 준다
        }
        try
        {
            System.Windows.Clipboard.SetText(md);
            _main.Status = $"'{selected.Name}' 요약(마크다운 {md.Split(Environment.NewLine).Length}줄)을 클립보드에 복사했습니다.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _main.Status = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다. 잠시 후 다시 시도하세요.";
        }
    }

    /// <summary>지운 제공 팩 창(다시 넣기).</summary>
    [RelayCommand]
    private void ShowDeletedPresets()
    {
        var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current.MainWindow;
        new Views.DeletedPresetsWindow(_main) { Owner = owner }.Show();
    }

    /// <summary>선택 레시피의 버전 목록 창(비교·되돌리기·새 레시피로 복제).</summary>
    [RelayCommand]
    private void ShowVersions()
    {
        if (SelectedRecipe is not { } selected) { _main.Status = "버전을 볼 레시피를 고르세요."; return; }
        var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current.MainWindow;
        new Views.RecipeVersionsWindow(this, selected) { Owner = owner }.Show();
    }

    /// <summary>버전을 지금 레시피에 되돌린다(id는 그대로). 되돌리기 전 내용도 버전으로 남기고, 제공 팩이면 번들 소스에도 되쓴다.</summary>
    public void RestoreVersion(Recipe target, Recipe version, DateTime versionTime)
    {
        _main.RecordRecipeVersion(target, "되돌리기 전");
        target.Name = version.Name;
        target.Category = version.Category;
        target.Labels = version.Labels.ToList();
        target.DefaultLineCount = version.DefaultLineCount;
        target.Slots = RecipeMeasure.Clone(version).Slots;
        if (!ReferenceEquals(SelectedRecipe, target) && FilteredRecipes.Contains(target)) SelectedRecipe = target;
        RebuildPreview();
        RefreshBadges();
        RefreshFilter();
        _main.SaveRecipeLibrary();
        WarnIfBundled();
        _main.Status = $"'{target.Name}'을(를) {versionTime:MM-dd HH:mm} 버전으로 되돌렸습니다(되돌리기 전 내용도 버전에 남겼습니다).";
    }

    /// <summary>버전을 새 레시피로 복제한다(지금 레시피는 그대로).</summary>
    public void CloneVersionAsNew(Recipe source, Recipe version, DateTime versionTime)
    {
        var copy = RecipeMeasure.Clone(version);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = $"{version.Name} ({versionTime:MM-dd HH:mm} 버전)";
        var insertAt = Recipes.IndexOf(source) + 1;
        Recipes.Insert(insertAt > 0 ? insertAt : Recipes.Count, copy);
        SyncToMain();
        RefreshBadges();
        RefreshFilter();
        if (FilteredRecipes.Contains(copy)) SelectedRecipe = copy;
        _main.Status = $"'{copy.Name}'(으)로 복제했습니다.";
    }

    /// <summary>두 레시피 비교 리포트를 띄운다(⚖ 비교·버전 비교 공용).</summary>
    public async Task ShowComparisonAsync(Recipe a, string nameA, Recipe b, string nameB)
    {
        var ca = RecipeMeasure.Clone(a);
        var cb = RecipeMeasure.Clone(b);
        var pools = _main.Pools.ToDictionary(p => p.Id);
        var opts = _main.Generation.SnapshotOptions();
        var (rules, tags) = (_main.Conflicts, _main.TagInfo);
        _main.Status = "레시피 비교 중…";
        try
        {
            var (pa, pb) = await Task.Run(() => (
                Core.Generation.RecipeProfiler.Measure(ca, pools, opts, rules, tags),
                Core.Generation.RecipeProfiler.Measure(cb, pools, opts, rules, tags)));
            var text = Core.Output.RecipeProfileText.Compare(pa, nameA, pb, nameB, opts.Seed);
            var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                        ?? System.Windows.Application.Current.MainWindow;
            new Views.ReportWindow("레시피 비교", text) { Owner = owner }.Show();
            _main.Status = "레시피 비교 완료";
        }
        catch (Core.Generation.GenerationValidationException ex) { _main.Status = "비교할 수 없음 — 검증 오류: " + ex.Message; }
        catch (InvalidOperationException ex) { _main.Status = "비교 중 오류(다시 시도하세요): " + ex.Message; }
    }

    /// <summary>수정 전/후 비교. Ctrl로 두 레시피를 골랐으면 목록에서 앞에 있는 쪽이 A(보통 옛 버전 — 새 id는 뒤에 붙는다),
    /// 하나만 골랐으면 그 저장본(A)과 레시피 빌더의 현재 구성(B). 같은 시드·생성 탭 옵션으로 잰다(훅 제외).</summary>
    [RelayCommand]
    private async Task CompareSelected()
    {
        var picked = _multiSelected.Distinct().ToList();
        Recipe a, b;
        string nameA, nameB;
        if (picked.Count == 2)
        {
            picked.Sort((x, y) => Recipes.IndexOf(x).CompareTo(Recipes.IndexOf(y)));
            (a, b) = (picked[0], picked[1]);
            (nameA, nameB) = (a.Name, b.Name);
        }
        else if (picked.Count <= 1 && SelectedRecipe is { } selected)
        {
            (a, b) = (selected, _main.RecipeBuilder.BuildRecipe());
            (nameA, nameB) = ($"{selected.Name} (저장본)", "레시피 빌더의 현재 구성");
        }
        else
        {
            _main.Status = "비교할 레시피를 고르세요 — 하나(빌더의 현재 구성과 비교) 또는 Ctrl로 두 개.";
            return;
        }

        await ShowComparisonAsync(a, nameA, b, nameB);
    }


    /// <summary>전체 검사 결과 요약 + 진행률. 검사 중에는 진행률이, 끝나면 요약이 들어간다.</summary>
    [ObservableProperty] private string _auditSummary = "";
    /// <summary>전체 검사에서 지적이 나온 팩만. 클릭하면 그 팩으로 이동한다.</summary>
    public ObservableCollection<AuditRow> AuditRows { get; } = new();
    /// <summary>검사 결과 목록을 보여줄지(지적이 하나라도 있을 때만). AuditRows를 채우거나
    /// 비운 뒤 직접 통지한다 — 컬렉션 개수 변화는 바인딩이 알아서 알려주지 않는다.</summary>
    public bool HasAuditRows => AuditRows.Count > 0;
    [ObservableProperty] private AuditRow? _selectedAuditRow;

    partial void OnSelectedAuditRowChanged(AuditRow? value)
    {
        if (value == null) return;
        // 검사 결과에서 팩을 고르면 필터를 넘어 그 팩을 바로 띄운다(필터에 가려 안 보일 수 있다).
        var target = Recipes.FirstOrDefault(r => r.Id == value.RecipeId);
        if (target == null) return;
        if (!FilteredRecipes.Contains(target))
        {
            SearchText = "";
            SelectedCategory = AllCategoriesLabel;
            RecipeFilter = NsfwFilterMode.All;
            ShowFavoritesOnly = false;
        }
        SelectedRecipe = target;
    }

    /// <summary>선택 레시피의 슬롯별 실제 내용물(인라인 태그 + 참조 풀 후보). 미리보기가 바라본다.</summary>
    public ObservableCollection<SlotPreviewViewModel> SelectedRecipePreview { get; } = new();

    /// <summary>선택 레시피의 자유 라벨(Recipe.Labels)을 쉼표 구분 문자열로 편집하는 패스스루.
    /// danbooru 태그가 아니라 컨셉 분류용 — LostFocus로 커밋(매 키 입력마다 쪼개면 중간 상태가
    /// 깨진다). 커밋되면 즉시 저장하고, 일괄 생성 패널의 라벨 필터 칩 목록도 다시 채운다.</summary>
    public string SelectedRecipeLabelsText
    {
        get => SelectedRecipe is null ? "" : string.Join(", ", SelectedRecipe.Labels);
        set
        {
            if (SelectedRecipe is null) return;
            SelectedRecipe.Labels = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            OnPropertyChanged();
            _main.SaveRecipeLibrary();
            _main.Generation.RefreshBatchRecipes();
            WarnIfBundled();
        }
    }

    /// <summary>분류 편집 드롭다운의 후보(필터용 Categories에서 "(전체)"를 뺀 것). 편집 가능
    /// 콤보라 목록에 없는 새 분류도 그대로 타이핑해 넣을 수 있다.</summary>
    public ObservableCollection<string> EditableCategories { get; } = new();

    /// <summary>선택 레시피의 분류(Recipe.Category) 편집 패스스루. 지금까지 이 값은 필터로
    /// 쓰이기만 하고 앱 안에서 고칠 방법이 없어 JSON을 직접 열어야 했다. 커밋되면 즉시
    /// 저장하고 분류 후보 목록·필터를 다시 채운다.</summary>
    public string SelectedRecipeCategory
    {
        get => SelectedRecipe?.Category ?? "";
        set
        {
            if (SelectedRecipe is null) return;
            var next = (value ?? "").Trim();
            if (SelectedRecipe.Category == next) return;
            SelectedRecipe.Category = next;
            OnPropertyChanged();
            _main.SaveRecipeLibrary();
            WarnIfBundled();
            var keep = SelectedRecipe;
            RefreshCategories();
            RefreshFilter();
            if (FilteredRecipes.Contains(keep)) SelectedRecipe = keep;
        }
    }

    public RecipeLibraryViewModel(MainViewModel main)
    {
        _main = main;
        Recipes = new ObservableCollection<Recipe>(main.SavedRecipes);
        // 저장해 둔 정렬·묶기(필드에 직접 — 변경 알림으로 설정을 되쓰지 않게).
        _sortMode = SortModes.Contains(main.Settings.RecipeSortMode) ? main.Settings.RecipeSortMode : SortDefault;
        _groupByCategory = main.Settings.RecipeGroupByCategory;
        RefreshBadges();
        RefreshCategories();
        RefreshFilter();
        ApplyGrouping();
    }

    partial void OnRecipeFilterChanged(NsfwFilterMode value) => RefreshFilter();

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    partial void OnShowFavoritesOnlyChanged(bool value) => RefreshFilter();

    partial void OnShowChangedOnlyChanged(bool value) => RefreshFilter();

    partial void OnSortModeChanged(string value)
    {
        _main.Settings.RecipeSortMode = value;
        RefreshFilter();
    }

    partial void OnGroupByCategoryChanged(bool value)
    {
        _main.Settings.RecipeGroupByCategory = value;
        ApplyGrouping();
    }

    /// <summary>목록 보기(ICollectionView)에 분류 묶음을 걸거나 푼다. 묶음 순서는 정렬된 목록에서 그 분류가 처음 나오는 순서다.</summary>
    private void ApplyGrouping()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FilteredRecipes);
        view.GroupDescriptions.Clear();
        if (GroupByCategory)
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Recipe.Category), new CategoryGroupConverter()));
    }

    private sealed class CategoryGroupConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string s && s.Length > 0 ? s : "(미분류)";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>정렬 키를 한 번에 모은다(최근 수정은 버전 폴더를 훑으므로 정렬할 때만).</summary>
    private Func<Recipe, IComparable> SortKey(IReadOnlyList<Recipe> recipes)
    {
        switch (SortMode)
        {
            case SortRecentlyAdded:
            {
                var index = new Dictionary<Recipe, int>(ReferenceEqualityComparer.Instance);
                for (int i = 0; i < Recipes.Count; i++) index[Recipes[i]] = i;
                return r => -index.GetValueOrDefault(r);
            }
            case SortRecentlyChanged:
            {
                // Recipe는 Equals를 재정의하지 않아 기본 비교가 곧 참조 비교다.
                var changed = recipes.ToDictionary(r => r, r => Core.Persistence.RecipeHistoryStore.LastChanged(AppPaths.HistoryDir, r.Id) ?? DateTime.MinValue);
                return r => -changed[r].Ticks;
            }
            case SortRecentlyUsed:
                return r => -(_main.Settings.RecipeLastUsed.TryGetValue(r.Id, out var t) ? t.Ticks : 0);
            case SortByName:
                return r => r.Name.StartsWith("🔞", StringComparison.Ordinal) ? r.Name[2..].TrimStart() : r.Name;
            default:
                return _ => 0;
        }
    }

    /// <summary>🆕/✏️ 표시를 전부 지운다("다 봤다").</summary>
    [RelayCommand]
    private void ClearChangeMarks()
    {
        _main.ClearRecipeChangeMarks();
        RefreshBadges();
        ShowChangedOnly = false;
        RefreshFilter();
    }

    partial void OnSelectedRecipeChanged(Recipe? value)
    {
        RebuildPreview();
        RefreshValidation();
        QuickPreviewText = "";
        SelectedRecipeIsFavorite = value != null && _main.Settings.FavoriteRecipeIds.Contains(value.Id);
        // 열어 봤으니 🆕/✏️ 표시를 지운다. 목록의 배지 글자는 다음 목록 갱신 때 사라진다(지금 지우면 방금 고른 게 무엇이었는지 헷갈린다).
        if (value != null && _main.MarkRecipeSeen(value.Id)) RefreshChangedCount();
        // 버전 기록의 기준점: 고르기만 할 땐 파일을 안 쓰고 고치기 전 내용을 메모리에 잡아 둔다 — 첫 편집 때 남긴다(WarnIfBundled).
        _baseline = value != null ? (value.Id, Core.Persistence.RecipeHistoryStore.Serialize(value)) : null;
        OnPropertyChanged(nameof(SelectedRecipeLabelsText));
        OnPropertyChanged(nameof(SelectedRecipeCategory));
    }

    /// <summary>선택 레시피의 미리보기를 다시 채운다. 풀 참조 슬롯은 풀 이름과 그 후보까지
    /// 풀어서 보여준다(예전엔 인라인 Tags만 세서 "후보 0개"로 보였음). 각 항목은 편집 가능
    /// 뷰모델이라 태그 삭제/추가가 라이브러리에 저장된 레시피에 바로 반영된다.</summary>
    private void RebuildPreview()
    {
        SelectedRecipePreview.Clear();
        if (SelectedRecipe == null) return;

        foreach (var slot in SelectedRecipe.Slots)
            SelectedRecipePreview.Add(new SlotPreviewViewModel(slot, ResolveSlotPools(slot), _main.TagDb, WarnIfBundled));
    }

    /// <summary>선택 레시피에 새 슬롯을 하나 추가하고 공통 뒷정리(미리보기·배지 갱신, 저장,
    /// 번들 소스 되쓰기)를 한다 — 슬롯 3종(고정/랜덤 풀/대안) 추가 명령이 모두 이걸 거친다.
    /// 태그 축(순서 조합 생성기 등)은 순서에 민감해 프롬프트 앞뒤 배치가 의미를 가지므로,
    /// 슬롯 추가만 있고 옮기는 기능이 없으면 새로 넣은 축을 항상 맨 끝에만 둘 수밖에 없다
    /// — 그래서 MoveSlotUp/Down도 같이 추가했다(아래).</summary>
    private void AddSlotToSelected(Slot slot, string statusVerb)
    {
        if (SelectedRecipe == null)
        {
            _main.Status = "슬롯을 추가할 레시피를 먼저 선택하세요.";
            return;
        }
        SelectedRecipe.Slots.Add(slot);
        RebuildPreview();
        RefreshBadges();
        _main.SaveRecipeLibrary();
        WarnIfBundled();
        _main.Status = $"{statusVerb} 슬롯을 추가했습니다 — 맨 아래 펼쳐서 채우세요.";
    }

    [RelayCommand]
    private void AddFixedSlotToSelected() =>
        AddSlotToSelected(new FixedSlot { Label = "새 고정" }, "새 고정");

    [RelayCommand]
    private void AddRandomPoolSlotToSelected() =>
        AddSlotToSelected(new RandomPoolSlot { Label = "새 랜덤 풀", MinCount = 0, MaxCount = 1 }, "새 랜덤 풀");

    /// <summary>빈 그룹 2개로 시작(RecipeBuilderViewModel.AddAlternativeSlot과 동일 패턴) —
    /// 그룹이 하나도 없으면 검증에서 막히고, 라이브러리 미리보기는 지금 대안 그룹 안의
    /// 태그까지는 편집을 못 지원하니(헤더만 보임) 그룹 태그는 레시피 빌더에서 채워야 한다.</summary>
    [RelayCommand]
    private void AddAlternativeSlotToSelected()
    {
        var slot = new AlternativeSlot { Label = "새 대안" };
        slot.Groups.Add(new AlternativeGroup { Label = "방법 A" });
        slot.Groups.Add(new AlternativeGroup { Label = "방법 B" });
        AddSlotToSelected(slot, "새 대안");
    }

    /// <summary>미리보기에 뜬 순서 = Recipe.Slots의 실제 순서 = 생성될 때 태그가 나열되는
    /// 순서(TagOrdering이 분류 못 하는 미분류 태그는 원래 위치를 그대로 쓰므로, 슬롯 순서
    /// 자체가 그 태그들의 최종 위치를 결정한다). 그래서 슬롯 추가만 되고 순서를 못 바꾸면
    /// 항상 맨 끝에만 낄 수 있다 — 위/아래로 인접 슬롯과 맞바꾼다.</summary>
    [RelayCommand]
    private void MoveSlotUp(SlotPreviewViewModel item) => MoveSlotBy(item, -1);

    [RelayCommand]
    private void MoveSlotDown(SlotPreviewViewModel item) => MoveSlotBy(item, +1);

    [RelayCommand]
    private void MoveSlotToTop(SlotPreviewViewModel item) => MoveSlotTo(item, 0);

    [RelayCommand]
    private void MoveSlotToBottom(SlotPreviewViewModel item) =>
        MoveSlotTo(item, (SelectedRecipe?.Slots.Count ?? 1) - 1);

    private void MoveSlotBy(SlotPreviewViewModel item, int delta)
    {
        if (SelectedRecipe == null) return;
        int i = SelectedRecipe.Slots.IndexOf(item.UnderlyingSlot);
        if (i < 0) return;
        MoveSlotTo(item, i + delta);
    }

    /// <summary>슬롯을 targetIndex 위치로 옮긴다(맨 위/맨 아래 버튼과 드래그 드롭이 공용으로 씀).
    /// 범위를 벗어나면 가장 가까운 끝으로 클램프 — "맨 위" 버튼이 이미 맨 위인 슬롯에 눌려도
    /// 조용히 무시되게.</summary>
    private void MoveSlotTo(SlotPreviewViewModel item, int targetIndex)
    {
        if (SelectedRecipe == null) return;
        var slots = SelectedRecipe.Slots;
        int i = slots.IndexOf(item.UnderlyingSlot);
        if (i < 0) return;
        int j = Math.Clamp(targetIndex, 0, slots.Count - 1);
        if (i == j) return;
        var slot = slots[i];
        slots.RemoveAt(i);
        slots.Insert(j, slot);
        RebuildPreview();
        _main.SaveRecipeLibrary();
        WarnIfBundled();
    }

    /// <summary>드래그 앤 드롭으로 슬롯 순서를 바꾼다. 코드비하인드(RecipeLibraryWindow.xaml.cs)가
    /// 드래그 소스/드롭 대상의 SlotPreviewViewModel을 찾아 여기로 넘긴다 — 드래그 "중"에는
    /// 아무것도 갱신하지 않고 "놓는 순간"에만 한 번 호출된다(매 MouseMove마다 리스트를 다시
    /// 그리면 드래그가 심하게 끊기고 버벅여서, 드롭 시점에만 반영하도록 설계).</summary>
    public void ReorderSlot(SlotPreviewViewModel dragged, SlotPreviewViewModel dropTarget)
    {
        if (SelectedRecipe == null || ReferenceEquals(dragged, dropTarget)) return;
        int targetIndex = SelectedRecipe.Slots.IndexOf(dropTarget.UnderlyingSlot);
        if (targetIndex < 0) return;
        MoveSlotTo(dragged, targetIndex);
    }

    /// <summary>실수로 추가한 슬롯을 통째로 지운다. 확인창 없이 즉시 — 태그 하나 지우는 것과
    /// 같은 급의 작업이라(RemoveTag도 확인 없음) 굳이 막지 않는다. 슬롯 자체가 아까우면
    /// '복제'로 레시피를 먼저 통째로 백업해 두면 된다.</summary>
    [RelayCommand]
    private void RemoveSlotFromSelected(SlotPreviewViewModel item)
    {
        if (SelectedRecipe == null) return;
        if (SelectedRecipe.Slots.Remove(item.UnderlyingSlot))
        {
            RebuildPreview();
            RefreshBadges();
            _main.SaveRecipeLibrary();
            WarnIfBundled();
            _main.Status = "슬롯을 삭제했습니다.";
        }
    }

    /// <summary>슬롯이 참조하는 풀들을 전부 모은다(PoolId 하나 + 체이닝된 ExtraPoolIds).
    /// 미리보기·모순 배지·NSFW 판정이 공통으로 쓴다.</summary>
    private List<Pool> ResolveSlotPools(Slot slot)
    {
        if (slot is not RandomPoolSlot r) return new List<Pool>();
        var result = new List<Pool>();
        if (!string.IsNullOrEmpty(r.PoolId))
        {
            var p = _main.Pools.FirstOrDefault(p => p.Id == r.PoolId);
            if (p != null) result.Add(p);
        }
        foreach (var extraId in r.ExtraPoolIds)
        {
            var p = _main.Pools.FirstOrDefault(p => p.Id == extraId);
            if (p != null) result.Add(p);
        }
        return result;
    }

    /// <summary>번들(제공) 팩을 앱 안에서 고쳤을 때, 그 수정을 번들 소스 파일에도 바로 되써서
    /// 다음 프리셋 갱신 때 도로 원복되지 않게 한다. 예전엔(git으로 배포되던 시절) 다른 사용자의
    /// 커스터마이징을 보호하려고 일부러 "번들 갱신이 항상 이김"으로 만들어 뒀는데, 지금은
    /// 이 데이터가 git으로 배포되지 않고 로컬 1인 사용이라 그 보호가 오히려 "앱에서 고쳐도
    /// 저장이 안 된다"는 혼란만 준다 — 그래서 소스 자체를 갱신해 워처가 같은 내용을 다시
    /// 읽어도 결과가 그대로 유지되게 한다.
    /// exe 옆 사본(AppPaths.PresetRecipesDir)뿐 아니라, 같은 체크아웃 안에 있으면 저장소
    /// 원본(AppPaths.RepoSourceRecipesDir)에도 함께 쓴다 — "원본을 고쳐야" 한다는 요청 반영.
    /// 되돌리고 싶을 때를 위해 덮어쓰기 전 기존 파일을 .bak로 남긴다(둘 다).</summary>
    private void WarnIfBundled()
    {
        // 편집이 모두 여기를 지난다(태그 추가·삭제·슬롯 이동) — 버전을 남긴다(연속 편집은 합쳐진다).
        // 이 레시피의 첫 편집이면 먼저 고르던 때 내용을 기준 버전으로(마지막 버전과 같으면 안 쓴다).
        if (SelectedRecipe != null && _baseline is { } b && b.Id == SelectedRecipe.Id)
        {
            _main.RecordRecipeVersion(b.Json, b.Id, SelectedRecipe.Name, Core.Persistence.RecipeHistoryStore.BaselineReason);
            _baseline = null;
        }
        if (SelectedRecipe != null) _main.RecordRecipeVersion(SelectedRecipe, Core.Persistence.RecipeHistoryStore.EditReason);
        if (SelectedRecipe == null || !_main.IsBundled(SelectedRecipe.Id)) return;
        var fileName = SelectedRecipe.Id + ".json";
        var targets = new List<string> { Path.Combine(AppPaths.PresetRecipesDir, fileName) };
        if (AppPaths.RepoSourceRecipesDir is { } repoDir)
            targets.Add(Path.Combine(repoDir, fileName));
        // 개발 모드면 두 경로가 같은 파일이다 — 두 번 쓰지 않는다.
        targets = targets.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var failures = new List<string>();
        foreach (var path in targets)
        {
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
                JsonStore.SaveAtomic(SelectedRecipe, path);
            }
            catch (IOException ex) { failures.Add($"{path} ({ex.Message})"); }
        }
        _main.Status = failures.Count == 0
            ? $"📦 '{SelectedRecipe.Name}' 수정을 번들 소스에도 저장했습니다({targets.Count}곳, .bak 백업 남김)."
            : $"⚠ '{SelectedRecipe.Name}' 일부 저장 실패 — {string.Join("; ", failures)}";
    }

    [RelayCommand]
    private void SelectFilter(string mode)
    {
        RecipeFilter = mode switch
        {
            "sfw" => NsfwFilterMode.SfwOnly,
            "nsfw" => NsfwFilterMode.NsfwOnly,
            _ => NsfwFilterMode.All,
        };
    }

    /// <summary>SFW 팩을 먼저, NSFW 팩을 뒤에 보여준다(OrderBy는 안정 정렬이라 각 그룹
    /// 내부의 기존 순서는 그대로 유지된다). 성인/검색/카테고리/즐겨찾기 필터를 전부 AND로 통과한 것만.
    /// <para>"테스트" 라벨이 붙은 팩은 그보다 먼저 맨 아래로 내린다(RecipeDisplayOrder).
    /// SFW/NSFW 묶음보다 <b>앞선</b> 키여야 한다 — 뒤에 두면 자기가 속한 묶음 안에서만
    /// 마지막이 되어 반대편 묶음 팩들 위에 남는다. 일괄 생성 체크리스트도 같은 규칙을 쓴다.</para></summary>
    private void RefreshFilter()
    {
        FilteredRecipes.Clear();
        var search = SearchText.Trim();
        var filtered = Recipes
            .Where(r => string.IsNullOrWhiteSpace(search)
                || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || UsesTag(r, search))
            .Where(r => SelectedCategory == AllCategoriesLabel || r.Category == SelectedCategory)
            .Where(r => !ShowFavoritesOnly || _main.Settings.FavoriteRecipeIds.Contains(r.Id))
            .Where(r => !ShowChangedOnly || r.ChangeBadge.Length > 0)
            .Select(r => (Recipe: r, IsNsfw: IsNsfwRecipe(r)))
            .Where(x => RecipeFilter switch
            {
                NsfwFilterMode.SfwOnly => !x.IsNsfw,
                NsfwFilterMode.NsfwOnly => x.IsNsfw,
                _ => true,
            })
            .ToList();
        // "테스트" 라벨은 어떤 정렬에서도 맨 아래. 기본 순서는 예전 그대로(일반 → 성인, 목록 순서 — OrderBy는 안정 정렬).
        var key = SortMode == SortDefault ? null : SortKey(filtered.Select(x => x.Recipe).ToList());
        var ordered = (key is null
                ? filtered.OrderBy(x => RecipeDisplayOrder.IsPinnedToBottom(x.Recipe)).ThenBy(x => x.IsNsfw)
                : filtered.OrderBy(x => RecipeDisplayOrder.IsPinnedToBottom(x.Recipe)).ThenBy(x => key(x.Recipe)))
            .Select(x => x.Recipe);

        foreach (var r in ordered) FilteredRecipes.Add(r);

        if (SelectedRecipe != null && !FilteredRecipes.Contains(SelectedRecipe))
            SelectedRecipe = null;
    }

    /// <summary>카테고리 드롭다운 후보를 Recipes에서 다시 모은다(맨 앞에 "전체"를 뜻하는
    /// 빈 문자열 포함). 미분류(빈 Category)는 후보에 안 넣는다 — 필터 없음과 뜻이 겹치므로.</summary>
    private void RefreshCategories()
    {
        var selected = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategoriesLabel);
        foreach (var c in Recipes.Select(r => r.Category).Where(c => !string.IsNullOrEmpty(c))
                     .Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            Categories.Add(c);
        SelectedCategory = Categories.Contains(selected) ? selected : AllCategoriesLabel;

        EditableCategories.Clear();
        foreach (var c in Categories.Skip(1)) EditableCategories.Add(c);   // "(전체)"는 분류 이름이 아니다
    }

    /// <summary>선택 레시피를 즐겨찾기에 추가/제거한다. 즉시 디스크에 저장돼 재시작해도 유지된다.</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedRecipe == null) return;
        var id = SelectedRecipe.Id;
        var favorites = _main.Settings.FavoriteRecipeIds;
        if (favorites.Remove(id)) SelectedRecipeIsFavorite = false;
        else { favorites.Add(id); SelectedRecipeIsFavorite = true; }
        SettingsStore.Save(_main.Settings);
        if (ShowFavoritesOnly) RefreshFilter();
    }

    /// <summary>선택 레시피의 검증 결과를 다시 계산한다. 사전에 없는 태그와 상대 없는 행위
    /// 태그를 잡는다 — 예전엔 tools/*.py를 따로 돌려야만 알 수 있었다.</summary>
    private void RefreshValidation()
    {
        if (SelectedRecipe == null) { ValidationText = ""; return; }
        var issues = ValidateRecipe(SelectedRecipe);
        ValidationText = issues.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", issues.Select(i => i.Message));
    }

    /// <summary>켜 둔 생성 훅도 함께 검사한다 — 생성할 때 훅이 슬롯으로 끼워지므로 실제 출력과 같은 대상을 본다.</summary>
    private IReadOnlyList<Core.Generation.ValidationIssue> ValidateRecipe(Recipe recipe) =>
        Core.Generation.RecipeValidator.Validate(
            recipe, _main.Pools.ToDictionary(p => p.Id), _main.TagInfo, Suggest, _main.Hooks, _main.Requires, _main.Conflicts);

    /// <summary>오탈자와 비슷한 실제 태그 이름 3개까지. 태그 DB가 아직 없으면 빈 목록.</summary>
    private IReadOnlyList<string> Suggest(string tag) =>
        _main.TagDb?.SearchRanked(tag.Replace('_', ' '), 3).Select(t => t.Name).ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>라이브러리 전체를 훑어 지적이 있는 팩만 모은다. 228팩 × 태그 수천 개라
    /// UI를 막지 않도록 백그라운드에서 돌리고 진행률을 표시한다.</summary>
    [RelayCommand]
    private async Task AuditAll(CancellationToken token)
    {
        var snapshot = Recipes.ToList();
        var pools = _main.Pools.ToDictionary(p => p.Id);
        var lookup = _main.TagInfo;
        var hooks = _main.Hooks.ToList();   // 백그라운드에서 읽으므로 사본
        var requires = _main.Requires;
        var conflicts = _main.Conflicts;
        AuditRows.Clear();
        OnPropertyChanged(nameof(HasAuditRows));
        AuditSummary = "검사 중…";

        var opts = _main.Generation.SnapshotOptions();
        var il = Core.Tokenization.TokenCounters.For(Core.Tokenization.ModelKind.IL, AppPaths.TokenizersDir);
        var anima = Core.Tokenization.TokenCounters.For(Core.Tokenization.ModelKind.Anima, AppPaths.TokenizersDir);
        try
        {
            // 🩺 점검·tools/RecipeCheck와 같은 기준(RecipeCheckReport, 등장률 분석은 빼고). 레시피끼리는 독립이라 병렬로.
            var found = await Task.Run(() =>
            {
                var rows = new AuditRow?[snapshot.Count];
                int done = 0;
                Parallel.For(0, snapshot.Count, new ParallelOptions { CancellationToken = token }, i =>
                {
                    try
                    {
                        var (summary, text) = Core.Generation.RecipeCheckReport.Build(snapshot[i], pools, opts, conflicts, lookup, requires, hooks,
                            Suggest, deep: false, sampleLines: 300, il: il, anima: anima);
                        if (summary.MustFix > 0) rows[i] = new AuditRow(snapshot[i].Id, snapshot[i].Name, summary.MustFix, text);
                    }
                    catch (Core.Generation.GenerationValidationException ex)
                    {
                        rows[i] = new AuditRow(snapshot[i].Id, snapshot[i].Name, 1, "생성할 수 없음 — " + ex.Message);
                    }
                    int n = Interlocked.Increment(ref done);
                    if (n % 20 == 0)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => AuditSummary = $"검사 중… {n}/{snapshot.Count}");
                });
                return rows.Where(r => r != null).Select(r => r!).OrderByDescending(r => r.MustFix).ToList();
            }, token);

            foreach (var row in found) AuditRows.Add(row);
            OnPropertyChanged(nameof(HasAuditRows));
            AuditSummary = found.Count == 0
                ? $"✅ {snapshot.Count}개 팩 전부 손볼 것 없음"
                : $"⚠ {snapshot.Count}개 중 {found.Count}개 팩에 손볼 것 {found.Sum(r => r.MustFix)}건(검증·항상 모순·조합 빠듯)";
        }
        catch (OperationCanceledException) { AuditSummary = "검사 취소됨"; }
    }

    /// <summary>빌더 상태를 건드리지 않고 선택 레시피만으로 5줄 뽑아 보여준다. 생성 카드와 같은 설정으로 뽑는다
    /// (<see cref="GenerationViewModel.SampleLines"/>) — 예전엔 따로 정한 기본값(균등 추첨·자동 정렬 끔·훅 없음)이라
    /// 같은 레시피인데 카드 미리보기·실제 파일과 태그 순서·구성이 다르게 보였다.</summary>
    [RelayCommand]
    private void QuickPreview()
    {
        if (SelectedRecipe == null) return;
        try
        {
            var (lines, seed) = _main.Generation.SampleLines(SelectedRecipe, 5);
            QuickPreviewText = string.Join("\n", lines);
            _main.Status = $"샘플 {lines.Count}줄 — 생성 카드 설정 그대로 · 시드 {seed}";
        }
        catch (Core.Generation.GenerationValidationException ex)
        {
            QuickPreviewText = "검증 오류: " + ex.Message;
        }
    }

    /// <summary>레시피가 이 태그(부분 일치, 대소문자 무시)를 인라인이나 참조 풀 후보로
    /// 쓰는지 검사한다. 검색창에 이름 대신 태그를 넣어도 걸리게 하려고 RefreshFilter가 쓴다
    /// — 오늘처럼 "이 태그 쓰는 레시피가 뭐가 있더라"를 grep 없이 앱에서 바로 찾기 위함.</summary>
    private bool UsesTag(Recipe recipe, string search)
    {
        foreach (var slot in recipe.Slots)
        {
            IEnumerable<string> tags = slot switch
            {
                FixedSlot f => f.Tags,
                RandomPoolSlot rp => rp.Tags,
                AlternativeSlot alt => alt.Groups.SelectMany(g => g.Tags),
                _ => Array.Empty<string>(),
            };
            if (tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))) return true;

            foreach (var pool in ResolveSlotPools(slot))
                if (pool.Candidates.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    return true;
        }
        return false;
    }

    /// <summary>레시피 성인 여부를 이름이 아니라 내용물로 판정한다: 슬롯의 인라인 태그나
    /// 참조하는 풀 후보 중 하나라도 ko-nsfw 출처(IsNsfw)면 성인. 사용자가 만든 레시피에도
    /// 이름 규칙 없이 그대로 통한다.</summary>
    private bool IsNsfwRecipe(Recipe recipe)
    {
        foreach (var slot in recipe.Slots)
        {
            IEnumerable<string> tags = slot switch
            {
                FixedSlot f => f.Tags,
                RandomPoolSlot rp => rp.Tags,
                AlternativeSlot alt => alt.Groups.SelectMany(g => g.Tags),
                _ => Array.Empty<string>(),
            };
            if (tags.Any(t => _main.TagInfo?.Lookup(t)?.IsNsfw == true)) return true;

            foreach (var pool in ResolveSlotPools(slot))
                if (pool.Candidates.Any(t => _main.TagInfo?.Lookup(t)?.IsNsfw == true))
                    return true;
        }
        return false;
    }

    /// <summary>JSON 왕복으로 완전히 독립된 복사본을 만든다. 슬롯은 참조 타입이라, 복사 없이
    /// 그대로 넣으면 나중에 빌더에서 편집할 때 라이브러리에 저장된 것까지 같이 바뀌어 버린다.</summary>
    private static Recipe Clone(Recipe recipe)
    {
        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        return JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;
    }

    /// <summary>Recipes 전체의 검증 배지(모순·시각 다양성)를 다시 계산한다. 목록이 통째로
    /// 바뀔 때(생성자, Refresh, 복제/새로 저장)만 부르면 되고, 검색어 입력 같은 필터링에서는
    /// 다시 계산할 필요 없다 — RefreshFilter는 이미 계산된 값을 그대로 보여주기만 한다.
    /// <para>계산은 레시피마다 독립이라 병렬로 돌린다. UI 스레드는 끝날 때까지 기다리므로 그동안 레시피·풀이
    /// 바뀔 일이 없고(읽기만 함), 결과 대입은 다 끝난 뒤 UI 스레드에서 한다.</para></summary>
    private void RefreshBadges()
    {
        var poolsById = _main.Pools.ToDictionary(p => p.Id);
        var recipes = Recipes.ToArray();
        var conflict = new string[recipes.Length];
        var variety = new string[recipes.Length];
        Parallel.For(0, recipes.Length, i =>
        {
            conflict[i] = ComputeConflictBadge(recipes[i], poolsById);
            variety[i] = ComputeVarietyBadge(recipes[i], poolsById);
        });
        for (int i = 0; i < recipes.Length; i++)
        {
            recipes[i].ConflictBadge = conflict[i];
            recipes[i].VarietyBadge = variety[i];
            recipes[i].OriginBadge = _main.IsBundled(recipes[i].Id) ? "📦" : "";
            recipes[i].ChangeBadge = _main.ChangeBadgeOf(recipes[i].Id);
        }
        RefreshChangedCount();
    }

    private void RefreshChangedCount()
    {
        var marks = _main.Settings.RecipeChangeMarks;
        int fresh = marks.Values.Count(m => m.Kind == Core.Persistence.RecipeChangeKind.New);
        int changed = marks.Count - fresh;
        ChangedCountText = marks.Count == 0 ? "" : string.Join(" · ",
            new[] { fresh > 0 ? $"🆕 {fresh}" : "", changed > 0 ? $"✏️ {changed}" : "" }.Where(x => x.Length > 0));
    }

    /// <summary>MAJOR 축 조합 수가 부족하면("이 팩은 매 줄이 비슷해 보인다") 배지를 붙인다.
    /// tools/visual_variety_scan.py를 포팅한 VarietyAnalyzer 재사용 — 기준(200)도 거기서 가져온다.</summary>
    private static string ComputeVarietyBadge(Recipe recipe, IReadOnlyDictionary<string, Core.Models.Pool> poolsById)
    {
        var major = Core.Generation.VarietyAnalyzer.ComputeMajorCombinations(recipe, poolsById);
        return major < Core.Generation.VarietyAnalyzer.Threshold ? $"🔸{major}" : "";
    }

    /// <summary>레시피 하나의 모순 배지 계산. RecipeBuilderViewModel.RefreshConflicts와 같은
    /// 방식(슬롯 단위, MaxCount 반영)으로 점검하되 문구 대신 목록용 짧은 기호만 돌려준다.</summary>
    private string ComputeConflictBadge(Recipe recipe, IReadOnlyDictionary<string, Core.Models.Pool> poolsById)
    {
        if (_main.Conflicts.Count == 0) return "";

        var hits = _main.Conflicts.AnalyzeRecipe(Core.Generation.SlotSpec.FromSlots(recipe.Slots, poolsById));
        if (hits.Count == 0) return "";
        return hits.Any(h => h.Severity == Core.Generation.ConflictSeverity.Definite) ? "‼" : "⚠";
    }

    /// <summary>현재 레시피 빌더의 슬롯 구성을 스냅샷 떠서 새 이름으로 라이브러리에 추가한다.
    /// 이름은 우선 기본값을 넣어두고, 오른쪽 텍스트박스에서 바로 고칠 수 있다(Pool의 "+풀"과 동일 패턴).</summary>
    [RelayCommand]
    private void SaveCurrentAsNew()
    {
        var snapshot = Clone(_main.RecipeBuilder.BuildRecipe());
        snapshot.Name = $"레시피 {Recipes.Count + 1}";
        var poolsById = _main.Pools.ToDictionary(p => p.Id);
        snapshot.ConflictBadge = ComputeConflictBadge(snapshot, poolsById);
        snapshot.VarietyBadge = ComputeVarietyBadge(snapshot, poolsById);
        Recipes.Add(snapshot);
        SyncToMain();
        RefreshCategories();
        RefreshFilter();
        SelectedRecipe = FilteredRecipes.Contains(snapshot) ? snapshot : SelectedRecipe;
        _main.Status = $"현재 구성을 '{snapshot.Name}'(으)로 저장했습니다.";
    }

    /// <summary>선택한 레시피를 통째로 복제해 바로 옆에 새 이름으로 추가한다. 불러오기 후
    /// "다른 이름으로 저장" 없이, 기존 팩을 베이스로 살짝만 바꾼 변형을 바로 만들고 싶을 때 쓴다.</summary>
    [RelayCommand]
    private void DuplicateSelected()
    {
        if (SelectedRecipe == null)
        {
            _main.Status = "복제할 레시피를 먼저 선택하세요.";
            return;
        }
        var copy = Clone(SelectedRecipe);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = $"{SelectedRecipe.Name} (사본)";
        // 슬롯 구성이 동일하니 배지도 그대로 복사(재계산 불필요).
        copy.ConflictBadge = SelectedRecipe.ConflictBadge;
        copy.VarietyBadge = SelectedRecipe.VarietyBadge;
        var insertAt = Recipes.IndexOf(SelectedRecipe) + 1;
        Recipes.Insert(insertAt, copy);
        SyncToMain();
        RefreshFilter();
        if (FilteredRecipes.Contains(copy)) SelectedRecipe = copy;
        _main.Status = $"'{copy.Name}'(으)로 복제했습니다.";
    }

    /// <summary>선택한 레시피를 빌더에 불러온다 — 빌더의 현재 슬롯 구성을 통째로 교체한다.
    /// 저장 안 한 현재 작업이 있으면 사라지지만, 레시피 빌더 자체가 창을 닫을 때 항상
    /// last-recipe.json에 자동 저장되므로 완전히 잃어버리진 않는다.</summary>
    [RelayCommand]
    private void LoadSelected()
    {
        if (SelectedRecipe == null)
        {
            _main.Status = "불러올 레시피를 먼저 선택하세요.";
            return;
        }
        var slotCount = SelectedRecipe.Slots.Count;
        _main.RecipeBuilder.LoadRecipe(SelectedRecipe);
        _main.MarkRecipesUsed(new[] { SelectedRecipe.Id });
        _main.ShowEditTab();
        _main.Status = $"레시피 '{SelectedRecipe.Name}' 불러옴 ({slotCount}개 슬롯)";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        // Pool과 동일한 이유로 확인창을 둔다: 되돌리기가 없고 창을 닫기만 해도 자동 저장된다.
        if (SelectedRecipe == null) return;
        var name = SelectedRecipe.Name;
        var confirm = System.Windows.MessageBox.Show(
            $"레시피 '{name}' ({SelectedRecipe.Slots.Count}개 슬롯)를 삭제할까요? 되돌릴 수 없습니다.",
            "레시피 삭제", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        Recipes.Remove(SelectedRecipe);
        SyncToMain();
        RefreshCategories();
        RefreshFilter();
        _main.Status = $"레시피 '{name}' 삭제됨";
    }

    /// <summary>코드비하인드(RecipeListBox_SelectionChanged)가 Ctrl/Shift 다중 선택을 옮겨줄 때 호출.</summary>
    public void SetMultiSelection(IEnumerable<Recipe> items)
    {
        _multiSelected.Clear();
        _multiSelected.AddRange(items);
    }

    /// <summary>Ctrl/Shift로 고른 여러 레시피(또는 단일 선택뿐이면 SelectedRecipe 하나)를
    /// 생성 패널의 "여러 레시피 일괄 생성" 체크 목록으로 그대로 보낸다. 기존 체크 상태는
    /// 이걸로 교체된다("방금 고른 것만 생성"이라는 기대에 맞춤 — 옛 체크가 섞여 나오면
    /// 오히려 헷갈린다). 라이브러리의 필터·검색으로 찾은 걸 생성 패널에서 또 찾을 필요가
    /// 없도록, 두 개로 나뉘어 있던 레시피 목록을 여기서 잇는다.</summary>
    [RelayCommand]
    private void SendSelectedToBatch()
    {
        var targets = _multiSelected.Count > 0 ? _multiSelected
            : SelectedRecipe != null ? new List<Recipe> { SelectedRecipe } : new List<Recipe>();
        if (targets.Count == 0)
        {
            _main.Status = "일괄 생성으로 보낼 레시피를 먼저 선택하세요(Ctrl/Shift로 여러 개 선택 가능).";
            return;
        }
        _main.Generation.SetBatchSelection(targets.Select(r => r.Id));
        _main.Status = $"{targets.Count}개 레시피를 일괄 생성 목록으로 보냈습니다 — 생성 패널에서 줄 수·시드를 확인하고 생성하세요.";
        System.Windows.Application.Current.MainWindow?.Activate();
    }

    [RelayCommand]
    private void Save() => Persist();

    /// <summary>MainViewModel.SavedRecipes가 바깥에서 교체된 뒤(예: 번들 프리셋 갱신) 목록을
    /// 다시 읽는다. Recipes는 생성자에서 뜬 마스터 사본이라, 다시 읽지 않으면 창을 닫을 때
    /// Persist가 옛 사본을 되써서 갱신이 통째로 무효가 된다.</summary>
    public void Refresh()
    {
        var selectedId = SelectedRecipe?.Id;
        Recipes.Clear();
        foreach (var r in _main.SavedRecipes) Recipes.Add(r);
        RefreshBadges();
        RefreshCategories();
        RefreshFilter();
        if (selectedId != null)
            SelectedRecipe = FilteredRecipes.FirstOrDefault(r => r.Id == selectedId);
    }

    /// <summary>Recipes 목록을 MainViewModel과 디스크에 반영. 저장 버튼뿐 아니라 라이브러리
    /// 창을 닫을 때도 호출돼(MainViewModel.OpenRecipeLibrary), 이름 변경이나 새로 추가한 걸
    /// 깜빡 잊고 저장 안 눌러도 사라지지 않게 하는 안전망 역할을 한다.</summary>
    public void Persist()
    {
        SyncToMain();
        _main.Status = "레시피 라이브러리 저장됨";
    }

    /// <summary>목록 구조 변경(새로 저장·복제·삭제)을 바로 MainViewModel·디스크·일괄 생성 목록에 반영한다.
    /// <para>왜: 예전엔 창을 닫을 때(Persist)까지 미뤘는데, 그사이 워처가 번들을 갱신하면 <see cref="Refresh"/>가
    /// MainViewModel.SavedRecipes로 목록을 다시 채워 방금 복제·저장한 레시피가 사라지고 지운 레시피가 되살아났다.
    /// 데이터를 다루는 스크립트를 앱을 켠 채 돌리는 작업 방식이라 실제로 겹치는 타이밍이다.</para></summary>
    private void SyncToMain()
    {
        _main.SavedRecipes.Clear();
        _main.SavedRecipes.AddRange(Recipes);
        _main.SaveRecipeLibrary();
        _main.Generation?.RefreshBatchRecipes();
    }

    /// <summary>모순 규칙 파일이 다시 읽힌 뒤 배지만 새 규칙으로 다시 계산한다(목록 구성은 그대로).</summary>
    public void RefreshConflictBadges()
    {
        var selectedId = SelectedRecipe?.Id;
        RefreshBadges();
        // Recipe는 변경 통지를 안 하므로 목록을 다시 채워야 배지가 다시 그려진다.
        RefreshFilter();
        if (selectedId != null && SelectedRecipe?.Id != selectedId)
            SelectedRecipe = FilteredRecipes.FirstOrDefault(r => r.Id == selectedId);
    }
}
