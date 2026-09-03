using System.Collections.ObjectModel;
using System.Text.Json;
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

    public SlotPreviewViewModel(Slot slot, IReadOnlyList<Pool> pools, TagDatabase? tagDb = null)
    {
        _slot = slot;
        _pools = pools;
        _tagDb = tagDb;
        InlineTags = slot switch
        {
            FixedSlot f => f.Tags,
            RandomPoolSlot r => r.Tags,
            _ => new System.Collections.ObjectModel.ObservableCollection<string>(),
        };
        RefreshHeader();
    }

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

    [ObservableProperty] private string _header = "";
    [ObservableProperty] private string _addTagText = "";

    /// <summary>추가 입력칸의 자동완성 후보(최대 8개). 콤마로 여러 개를 입력하는 중이면
    /// 마지막 조각만 검색어로 쓴다 — "1girl, sm" 이라고 치는 중이면 "sm"만 검색.</summary>
    public ObservableCollection<Tag> Suggestions { get; } = new();
    public bool HasSuggestions => Suggestions.Count > 0;

    partial void OnAddTagTextChanged(string value)
    {
        Suggestions.Clear();
        var lastPiece = value.Contains(',') ? value[(value.LastIndexOf(',') + 1)..].Trim() : value.Trim();
        if (_tagDb != null && lastPiece.Length > 0)
            foreach (var t in _tagDb.SearchRanked(lastPiece, 8)) Suggestions.Add(t);
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>자동완성 목록에서 하나를 클릭하면 입력칸의 마지막 조각을 그 태그로 바꿔
    /// 즉시 슬롯에 추가한다(오타 방지가 목적이므로 클릭 한 번으로 끝나야 함).</summary>
    [RelayCommand]
    private void SelectSuggestion(Tag tag)
    {
        var comma = AddTagText.LastIndexOf(',');
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
            RandomPoolSlot r => $"[랜덤] {r.Label} — 매줄 {r.MinCount}~{r.MaxCount}개 " +
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
    }

    [RelayCommand]
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(AddTagText)) return;
        foreach (var piece in AddTagText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!InlineTags.Contains(piece)) InlineTags.Add(piece);
        AddTagText = "";
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
        RefreshHeader();
    }
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
    /// <summary>선택 레시피가 즐겨찾기인지(버튼 라벨/상태 바인딩용). SelectedRecipe나
    /// 즐겨찾기 목록이 바뀔 때마다 갱신.</summary>
    [ObservableProperty] private bool _selectedRecipeIsFavorite;

    /// <summary>라이브러리 창을 안 거치고 바로 뽑아보는 짧은 샘플(5줄). 빌더 상태는 건드리지 않는다.</summary>
    [ObservableProperty] private string _quickPreviewText = "";

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
        }
    }

    public RecipeLibraryViewModel(MainViewModel main)
    {
        _main = main;
        Recipes = new ObservableCollection<Recipe>(main.SavedRecipes);
        RefreshBadges();
        RefreshCategories();
        RefreshFilter();
    }

    partial void OnRecipeFilterChanged(NsfwFilterMode value) => RefreshFilter();

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    partial void OnShowFavoritesOnlyChanged(bool value) => RefreshFilter();

    partial void OnSelectedRecipeChanged(Recipe? value)
    {
        RebuildPreview();
        QuickPreviewText = "";
        SelectedRecipeIsFavorite = value != null && _main.Settings.FavoriteRecipeIds.Contains(value.Id);
        OnPropertyChanged(nameof(SelectedRecipeLabelsText));
    }

    /// <summary>선택 레시피의 미리보기를 다시 채운다. 풀 참조 슬롯은 풀 이름과 그 후보까지
    /// 풀어서 보여준다(예전엔 인라인 Tags만 세서 "후보 0개"로 보였음). 각 항목은 편집 가능
    /// 뷰모델이라 태그 삭제/추가가 라이브러리에 저장된 레시피에 바로 반영된다.</summary>
    private void RebuildPreview()
    {
        SelectedRecipePreview.Clear();
        if (SelectedRecipe == null) return;

        foreach (var slot in SelectedRecipe.Slots)
            SelectedRecipePreview.Add(new SlotPreviewViewModel(slot, ResolveSlotPools(slot), _main.TagDb));
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
    /// 내부의 기존 순서는 그대로 유지된다). 성인/검색/카테고리/즐겨찾기 필터를 전부 AND로 통과한 것만.</summary>
    private void RefreshFilter()
    {
        FilteredRecipes.Clear();
        var search = SearchText.Trim();
        var ordered = Recipes
            .Where(r => string.IsNullOrWhiteSpace(search)
                || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || UsesTag(r, search))
            .Where(r => SelectedCategory == AllCategoriesLabel || r.Category == SelectedCategory)
            .Where(r => !ShowFavoritesOnly || _main.Settings.FavoriteRecipeIds.Contains(r.Id))
            .Select(r => (Recipe: r, IsNsfw: IsNsfwRecipe(r)))
            .Where(x => RecipeFilter switch
            {
                NsfwFilterMode.SfwOnly => !x.IsNsfw,
                NsfwFilterMode.NsfwOnly => x.IsNsfw,
                _ => true,
            })
            .OrderBy(x => x.IsNsfw)
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

    /// <summary>빌더 상태를 건드리지 않고 선택 레시피만으로 5줄 뽑아 보여준다. 생성 탭의
    /// 사용자 설정과 무관하게 합리적인 기본값(중복 회피·모순 회피 켜짐, 균등 추첨)을 쓴다 —
    /// "부족한 점 훑어보기" 용도라 매번 옵션을 맞출 필요가 없게.</summary>
    [RelayCommand]
    private void QuickPreview()
    {
        if (SelectedRecipe == null) return;
        try
        {
            var pools = _main.Pools.ToDictionary(p => p.Id);
            var opts = new Core.Models.GenerationOptions { LineCount = 5, Seed = Random.Shared.Next() };
            var generator = new Core.Generation.WildcardGenerator();
            var result = generator.Generate(SelectedRecipe, pools, opts, _main.Conflicts, _main.TagInfo);
            QuickPreviewText = string.Join("\n", result.Lines);
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
    /// 다시 계산할 필요 없다 — RefreshFilter는 이미 계산된 값을 그대로 보여주기만 한다.</summary>
    private void RefreshBadges()
    {
        var poolsById = _main.Pools.ToDictionary(p => p.Id);
        foreach (var r in Recipes)
        {
            r.ConflictBadge = ComputeConflictBadge(r);
            r.VarietyBadge = ComputeVarietyBadge(r, poolsById);
        }
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
    private string ComputeConflictBadge(Recipe recipe)
    {
        if (_main.Conflicts.Count == 0) return "";

        var specs = new List<Core.Generation.SlotSpec>();
        int nextAltOwnerId = 0;
        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue;
            if (slot is FixedSlot f)
            {
                specs.Add(new(f.Tags.ToList(), f.Tags.Count, IsFixed: true));
            }
            else if (slot is RandomPoolSlot r)
            {
                var tags = new List<string>(r.Tags);
                foreach (var pool in ResolveSlotPools(slot)) tags.AddRange(pool.Candidates);
                specs.Add(new(tags, r.MaxCount, IsFixed: false));
            }
            else if (slot is AlternativeSlot alt && alt.Groups.Count > 0)
            {
                // RecipeBuilderViewModel.RefreshConflicts와 동일한 이유로 그룹별로 쪼갠다.
                int ownerId = nextAltOwnerId++;
                foreach (var g in alt.Groups)
                    specs.Add(new(g.Tags.ToList(), g.Tags.Count, IsFixed: false, ownerId));
            }
        }

        var hits = _main.Conflicts.AnalyzeRecipe(specs);
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
        snapshot.ConflictBadge = ComputeConflictBadge(snapshot);
        snapshot.VarietyBadge = ComputeVarietyBadge(snapshot, poolsById);
        Recipes.Add(snapshot);
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
        _main.SavedRecipes.Clear();
        _main.SavedRecipes.AddRange(Recipes);
        _main.SaveRecipeLibrary();
        _main.Status = "레시피 라이브러리 저장됨";
    }
}
