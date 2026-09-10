using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.ViewModels;

public sealed partial class RecipeBuilderViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private Slot? _selectedSlot;
    [ObservableProperty] private string _conflictWarning = "";
    [ObservableProperty] private string _varietyWarning = "";
    /// <summary>검색 없이 슬롯에 직접 타이핑해 넣는 입력 칸. 태그 DB에 없는 자연어 구문
    /// 조각(예: "a girl standing in neon-lit rain")도 그대로 들어간다.</summary>
    [ObservableProperty] private string _customTagInput = "";
    /// <summary>현재 빌더에 불러온 레시피의 Id(있으면). "불러오기"로 채워지고, 슬롯을 손으로
    /// 새로 구성하기 시작해도 따로 지우진 않는다 — 그 상태에서 번들 프리셋을 갱신했을 때
    /// "지금 편집 중인 게 방금 갱신된 그 레시피"임을 알아야 자동으로 다시 불러올 수 있다
    /// (2026-08-26: 퀵갱신을 눌러도 이미 빌더에 열어 둔 내용은 안 바뀌던 문제의 원인).</summary>
    public string? LoadedRecipeId { get; private set; }
    public ObservableCollection<Slot> Slots { get; } = new();

    /// <summary>파괴적 편집(삭제·구성 교체)의 되돌리기 스택. 되돌릴 게 없으면 버튼이 꺼진다.</summary>
    private readonly Core.Models.RecipeEditHistory _history = new();
    public bool CanUndo => _history.CanUndo;
    /// <summary>되돌리기 버튼 툴팁 — 다음에 무엇이 되돌아오는지 이름으로 보여준다.</summary>
    public string UndoTooltip => _history.NextLabel is { } label
        ? $"되돌리기 (Ctrl+Z): {label}"
        : "되돌릴 편집이 없습니다";

    /// <summary>삭제/교체 지점마다 "되돌리는 법"을 등록한다. 되돌리는 도중에 일어난 컬렉션
    /// 변경은 RecipeEditHistory가 알아서 무시하므로 여기서 따로 막지 않아도 된다.</summary>
    private void Record(string label, Action undo)
    {
        _history.Push(label, undo);
        NotifyUndoState();
    }

    private void NotifyUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoTooltip));
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var label = _history.Undo();
        NotifyUndoState();
        if (label != null)
        {
            RefreshConflicts();
            _main.Status = "되돌림: " + label;
        }
    }
    public IReadOnlyList<Pool> Pools => _main.Pools;
    /// <summary>태그 칩 호버 툴팁이 설명/카테고리/빈도를 조회하는 데 쓴다.</summary>
    public Core.Generation.ITagLookup? TagInfo => _main.TagInfo;

    public RecipeBuilderViewModel(MainViewModel main)
    {
        _main = main;
        AttachTagSearch();
        // 슬롯이 추가/삭제되거나(수·풀·토글·태그가) 바뀔 때마다 모순 경고를 실시간 재계산.
        // MaxCount 숫자를 바꾸는 즉시 "이 풀에서 2개 이상 뽑으면 충돌" 여부가 반영된다.
        Slots.CollectionChanged += OnSlotsChanged;
        LoadOrSeed();
        RefreshConflicts();
    }

    /// <summary>Slots 컬렉션 변화에 맞춰 각 슬롯의 속성/태그 변경 구독을 갱신하고 모순을 재계산.</summary>
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Slot s in e.OldItems) Unsubscribe(s);
        if (e.NewItems != null) foreach (Slot s in e.NewItems) Subscribe(s);
        RefreshConflicts();
    }

    private void Subscribe(Slot s)
    {
        s.PropertyChanged += OnSlotPropertyChanged;
        switch (s)
        {
            case FixedSlot f:
                f.Tags.CollectionChanged += OnSlotTagsChanged;
                break;
            case RandomPoolSlot r:
                r.Tags.CollectionChanged += OnSlotTagsChanged;
                r.ExtraPoolIds.CollectionChanged += OnSlotTagsChanged;
                break;
            case AlternativeSlot alt:
                alt.Groups.CollectionChanged += OnAlternativeGroupsChanged;
                foreach (var g in alt.Groups) SubscribeGroup(g);
                break;
        }
    }

    private void Unsubscribe(Slot s)
    {
        s.PropertyChanged -= OnSlotPropertyChanged;
        switch (s)
        {
            case FixedSlot f:
                f.Tags.CollectionChanged -= OnSlotTagsChanged;
                break;
            case RandomPoolSlot r:
                r.Tags.CollectionChanged -= OnSlotTagsChanged;
                r.ExtraPoolIds.CollectionChanged -= OnSlotTagsChanged;
                break;
            case AlternativeSlot alt:
                alt.Groups.CollectionChanged -= OnAlternativeGroupsChanged;
                foreach (var g in alt.Groups) UnsubscribeGroup(g);
                break;
        }
    }

    private void SubscribeGroup(AlternativeGroup g) => g.Tags.CollectionChanged += OnSlotTagsChanged;
    private void UnsubscribeGroup(AlternativeGroup g) => g.Tags.CollectionChanged -= OnSlotTagsChanged;

    /// <summary>대안 슬롯의 그룹 자체가 추가/삭제될 때(그룹 안의 태그 변경이 아니라) 그 그룹의
    /// 태그 컬렉션 구독을 같이 걸고/떼고, 모순도 다시 계산한다.</summary>
    private void OnAlternativeGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (AlternativeGroup g in e.OldItems) UnsubscribeGroup(g);
        if (e.NewItems != null) foreach (AlternativeGroup g in e.NewItems) SubscribeGroup(g);
        RefreshConflicts();
    }

    // MaxCount/MinCount/PoolId/IsEnabled 중 하나라도 바뀌면 모순 경고를 다시 계산.
    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RandomPoolSlot.MaxCount) or nameof(RandomPoolSlot.MinCount)
            or nameof(RandomPoolSlot.PoolId) or nameof(Slot.IsEnabled))
            RefreshConflicts();
    }

    private void OnSlotTagsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshConflicts();

    /// <summary>마지막 저장 레시피가 있으면 그걸로 시작(재시작해도 작업이 남아 있게),
    /// 없거나 손상됐으면 이전과 같이 빈 고정 슬롯 하나로 시작.</summary>
    private void LoadOrSeed()
    {
        if (File.Exists(AppPaths.LastRecipeFile))
        {
            try
            {
                var recipe = RecipeStore.Load(AppPaths.LastRecipeFile);
                if (recipe.Slots.Count > 0)
                {
                    foreach (var s in recipe.Slots) Slots.Add(s);
                    SelectedSlot = Slots[0];
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                _main.Status = "저장된 레시피를 불러오지 못해 기본값으로 시작합니다: " + ex.Message;
            }
        }

        // 시작 시 빈 화면을 피하되 태그는 강제하지 않음(사용자가 직접 채움).
        var seed = new FixedSlot { Label = "기본" };
        Slots.Add(seed);
        SelectedSlot = seed;
    }

    /// <summary>직접 입력 칸의 내용을 선택 슬롯에 넣는다. 콤마로 여러 개를 한 번에 넣을 수 있고,
    /// DB에 없는 문자열도 허용한다 — 생성기는 태그 존재 여부를 강제하지 않는다.</summary>
    [RelayCommand]
    private void AddCustomTag()
    {
        if (string.IsNullOrWhiteSpace(CustomTagInput))
        {
            _main.Status = "직접 추가할 태그/문구를 먼저 입력하세요.";
            return;
        }
        foreach (var piece in CustomTagInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddTagToSelectedSlot(piece);
        CustomTagInput = "";
    }

    /// <summary>현재 슬롯 구성을 디스크에 저장한다. 저장 버튼과 창 닫기 시 자동 저장 둘 다에서 호출.</summary>
    [RelayCommand]
    private void SaveRecipe()
    {
        AppPaths.EnsureAppDataDir();
        RecipeStore.Save(BuildRecipe(), AppPaths.LastRecipeFile);
        _main.Status = "레시피 저장됨";
    }

    /// <summary>현재 레시피가 만들 수 있는 모순을 미리 점검해 경고 문구를 갱신한다.
    /// 고정 태그끼리는 '항상', 풀 후보까지 포함하면 '가끔' 충돌로 표시. 시각 다양성 경고도
    /// 슬롯이 바뀔 때마다 같이 갱신한다(호출 지점이 전부 겹치므로).</summary>
    public void RefreshConflicts()
    {
        RefreshVarietyWarning();

        var rules = _main.Conflicts;
        if (rules.Count == 0) { ConflictWarning = ""; return; }

        // 슬롯 단위로 넘겨 MaxCount를 반영한다: MaxCount=1인 풀 안의 상호배타 태그(예: 시선
        // 풀의 looking_up/looking_down)는 하나만 뽑히므로 서로 충돌로 잡히지 않는다.
        var specs = new List<Core.Generation.SlotSpec>();
        int nextAltOwnerId = 0;
        foreach (var slot in Slots)
        {
            if (!slot.IsEnabled) continue; // 꺼둔 슬롯은 출력 안 되니 모순 계산에서도 제외

            if (slot is FixedSlot f)
            {
                specs.Add(new(f.Tags.ToList(), f.Tags.Count, IsFixed: true));
            }
            else if (slot is RandomPoolSlot r)
            {
                // WildcardGenerator.ResolveCandidates와 동일하게 인라인 태그 + 체이닝된 풀(들) 후보를 합친다.
                var tags = new List<string>(r.Tags);
                if (!string.IsNullOrEmpty(r.PoolId))
                {
                    var pool = _main.Pools.FirstOrDefault(p => p.Id == r.PoolId);
                    if (pool != null) tags.AddRange(pool.Candidates);
                }
                foreach (var extraId in r.ExtraPoolIds)
                {
                    var extraPool = _main.Pools.FirstOrDefault(p => p.Id == extraId);
                    if (extraPool != null) tags.AddRange(extraPool.Candidates);
                }
                specs.Add(new(tags, r.MaxCount, IsFixed: false));
            }
            else if (slot is AlternativeSlot alt && alt.Groups.Count > 0)
            {
                // 그룹 하나가 통째로 뽑혀서 다른 그룹 태그와는 절대 안 섞인다. 그룹마다 별도
                // SlotSpec을 만들고 같은 AltGroupOwnerId를 부여해 AnalyzeRecipe가 "같은 슬롯의
                // 다른 그룹끼리는 동시 등장 불가"를 알게 한다(합쳐서 근사하면 오탐이 난다).
                int ownerId = nextAltOwnerId++;
                foreach (var g in alt.Groups)
                    specs.Add(new(g.Tags.ToList(), g.Tags.Count, IsFixed: false, ownerId));
            }
        }

        var hits = rules.AnalyzeRecipe(specs);
        if (hits.Count == 0) { ConflictWarning = ""; return; }

        var parts = hits.Select(h =>
            $"[{(h.Severity == Core.Generation.ConflictSeverity.Definite ? "항상" : "가끔")}] {h.Label}: {string.Join(" + ", h.Tags)}");
        ConflictWarning = "⚠ 모순 " + string.Join("  /  ", parts);
    }

    /// <summary>MAJOR 축 조합 수(VarietyAnalyzer, tools/visual_variety_scan.py 포팅)를 지금
    /// 만들고 있는(아직 저장 안 된) 슬롯 구성 그대로 다시 계산한다. 저장 후 라이브러리에서
    /// 확인할 필요 없이 빌더에서 바로 보이게 한다.</summary>
    private void RefreshVarietyWarning()
    {
        var poolsById = _main.Pools.ToDictionary(p => p.Id);

        var major = Core.Generation.VarietyAnalyzer.ComputeMajorCombinations(Slots, poolsById);
        VarietyWarning = major < Core.Generation.VarietyAnalyzer.Threshold
            ? $"🔸 MAJOR 축 조합 수 {major}개 (기준 {Core.Generation.VarietyAnalyzer.Threshold}개 미만 — 매 줄이 비슷해 보일 수 있습니다. 축을 추가하세요.)"
            : "";
    }

    /// <summary>태그 검색 결과를 이 빌더의 선택 슬롯으로 보내도록 콜백을 (재)연결.
    /// 풀 라이브러리 창이 콜백을 가져갔다가 닫힌 뒤 소유권을 되돌릴 때도 사용.</summary>
    public void AttachTagSearch() => _main.TagSearch.OnAddTag = AddTagToSelectedSlot;

    /// <summary>풀 라이브러리에서 풀이 추가/삭제된 뒤 슬롯의 풀 콤보를 갱신.</summary>
    public void RefreshPools() => OnPropertyChanged(nameof(Pools));

    /// <summary>랜덤 슬롯의 풀 콤보에서 풀을 고른 직후 호출(코드비하인드의 SelectionChanged가 트리거).
    /// 어떤 풀이 붙었는지 상태 메시지로 알려준다.</summary>
    public void NotifyPoolAttached(RandomPoolSlot slot)
    {
        if (string.IsNullOrEmpty(slot.PoolId)) return;
        var pool = _main.Pools.FirstOrDefault(p => p.Id == slot.PoolId);
        _main.Status = pool == null
            ? $"풀 '{slot.PoolId}'을 찾을 수 없습니다."
            : $"랜덤 [{slot.Label}] → 풀 '{pool.Name}' ({pool.Candidates.Count}개 후보)";
    }

    /// <summary>풀 체이닝: 슬롯에 풀을 하나 더 추가로 참조시킨다(이미 PoolId로 붙은 것이거나
    /// 이미 체이닝돼 있으면 무시). 같은 슬롯 안에서 여러 풀을 하나의 후보군으로 합쳐 뽑고
    /// 싶을 때 쓴다 — 예: 체위 풀 + 제압형 체위 풀을 한 슬롯으로 묶어 한 번에 하나만 뽑기.</summary>
    public void AddExtraPool(RandomPoolSlot slot, string poolId)
    {
        if (string.IsNullOrEmpty(poolId) || poolId == slot.PoolId || slot.ExtraPoolIds.Contains(poolId)) return;
        var pool = _main.Pools.FirstOrDefault(p => p.Id == poolId);
        slot.ExtraPoolIds.Add(poolId);
        _main.Status = pool == null
            ? $"풀 '{poolId}'을 찾을 수 없습니다."
            : $"랜덤 [{slot.Label}] → 풀 '{pool.Name}' 체이닝 추가 ({pool.Candidates.Count}개 후보)";
    }

    public void RemoveExtraPool(RandomPoolSlot slot, string poolId) => slot.ExtraPoolIds.Remove(poolId);

    public Recipe BuildRecipe() => new() { Name = "Untitled", Slots = Slots.ToList() };

    /// <summary>바깥에서 슬롯 구성을 통째로 갈아엎기 직전에 부른다(컨셉 빌더 위저드).
    /// 지금 구성을 되돌리기 스택에 넣어 두어, 실수로 눌러도 편집 중이던 내용을 되찾을 수 있게 한다.</summary>
    public void RecordSlotsBeforeReplace(string label)
    {
        var previous = Slots.ToList();
        if (previous.Count == 0) return;
        Record($"{label} (이전 구성 {previous.Count}슬롯)", () =>
        {
            Slots.Clear();
            foreach (var slot in previous) Slots.Add(slot);
            SelectedSlot = Slots.FirstOrDefault();
        });
    }

    /// <summary>레시피를 빌더에 통째로 불러온다(현재 슬롯 구성 교체) — 라이브러리의 "불러오기"와
    /// 퀵갱신 뒤 자동 재적재가 공유하는 경로. JSON 왕복으로 깊은 복사해 원본 Recipe 객체를
    /// 안 건드린다. <see cref="LoadedRecipeId"/>를 기록해 두면, 이 레시피가 나중에 번들 갱신으로
    /// 다시 바뀌었을 때 MainViewModel이 이 메서드를 다시 불러 화면을 최신으로 맞출 수 있다.</summary>
    public void LoadRecipe(Recipe recipe)
    {
        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        var copy = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;
        // 교체 직전 구성을 통째로 기억해 둔다 — "불러오기"는 지금까지 되돌릴 수 없는 채로
        // 편집 중이던 내용을 전부 날렸다.
        var previous = Slots.ToList();
        if (previous.Count > 0)
            Record($"'{recipe.Name}' 불러오기 (이전 구성 {previous.Count}슬롯)", () =>
            {
                Slots.Clear();
                foreach (var s in previous) Slots.Add(s);
                SelectedSlot = Slots.FirstOrDefault();
                LoadedRecipeId = null;
            });
        Slots.Clear();
        foreach (var slot in copy.Slots) Slots.Add(slot);
        SelectedSlot = Slots.FirstOrDefault();
        LoadedRecipeId = recipe.Id;
        RefreshConflicts();
    }

    /// <summary>선택된 슬롯 종류에 따라 태그를 그 슬롯의 후보로 넣는다:
    /// 랜덤 슬롯이면 그 슬롯의 후보 목록으로, 그 외에는 고정 슬롯으로.</summary>
    private void AddTagToSelectedSlot(string tag)
    {
        // 1) 랜덤 슬롯이 선택돼 있으면 그 슬롯의 후보 태그로 직접 넣는다(공용 풀 불필요).
        if (SelectedSlot is RandomPoolSlot rps)
        {
            if (!rps.Tags.Contains(tag))
            {
                rps.Tags.Add(tag);                      // ObservableCollection → UI 즉시 반영(칩 표시)
                RefreshConflicts();
                _main.Status = $"'{tag}' → 랜덤 [{rps.Label}] (후보 {rps.Tags.Count}개 중 무작위)" + LowFrequencySuffix(tag);
            }
            else
            {
                _main.Status = $"'{tag}' 은(는) 이미 랜덤 [{rps.Label}]에 있음";
            }
            return;
        }

        // 2) 그 외에는 고정 슬롯으로(선택된 고정 슬롯 → 마지막 고정 슬롯 → 새로 생성).
        var target = SelectedSlot as FixedSlot ?? Slots.OfType<FixedSlot>().LastOrDefault();
        if (target == null)
        {
            target = new FixedSlot { Label = "고정" };
            Slots.Add(target);
            SelectedSlot = target;
        }
        if (!target.Tags.Contains(tag))
        {
            target.Tags.Add(tag);                       // ObservableCollection → UI 즉시 반영
            RefreshConflicts();
            _main.Status = $"'{tag}' 추가됨 → 고정 [{target.Label}]" + LowFrequencySuffix(tag);
        }
        else
        {
            _main.Status = $"'{tag}' 은(는) 이미 [{target.Label}]에 있음";
        }
    }

    /// <summary>슬롯(고정/랜덤)의 태그들을 공용 풀로 저장한다(슬롯 우클릭 메뉴가 호출).
    /// 라이브러리 창과도 동기화하고 디스크에 저장한다.</summary>
    public void SaveSlotToPool(Slot? slot)
    {
        slot ??= SelectedSlot;
        var tags = slot switch
        {
            FixedSlot f => f.Tags,
            RandomPoolSlot r => r.Tags,
            _ => null
        };
        if (tags == null || tags.Count == 0)
        {
            _main.Status = "저장할 태그가 없는 슬롯입니다.";
            return;
        }
        var name = string.IsNullOrWhiteSpace(slot!.Label) ? $"풀 {_main.Pools.Count + 1}" : slot.Label;
        var pool = new Pool { Name = name };
        foreach (var t in tags) pool.Candidates.Add(t);
        _main.Pools.Add(pool);
        _main.PoolLibrary.Pools.Add(pool);   // 라이브러리 창 목록과 동기화
        _main.SavePools();
        RefreshPools();
        _main.Status = $"풀 '{name}' 저장됨 ({pool.Candidates.Count}개) → 풀 라이브러리";
    }

    /// <summary>고정/랜덤 슬롯에서 태그 하나 제거(칩의 ✕ 버튼이 호출).</summary>
    public void RemoveTagFromSlot(Slot slot, string tag)
    {
        var tags = slot switch
        {
            FixedSlot f => f.Tags,
            RandomPoolSlot r => r.Tags,
            _ => null
        };
        if (tags == null) return;
        int index = tags.IndexOf(tag);
        if (index < 0) return;
        tags.RemoveAt(index);
        Record($"태그 '{tag}' 삭제", () => tags.Insert(Math.Min(index, tags.Count), tag));
        RefreshConflicts();
        _main.Status = $"'{tag}' 제거됨 — 되돌리려면 ↶(Ctrl+Z)";
    }

    [RelayCommand]
    private void AddFixedSlot()
    {
        var s = new FixedSlot { Label = "고정" };
        Slots.Add(s);
        SelectedSlot = s;
        _main.Status = "고정 슬롯 추가됨";
    }

    [RelayCommand]
    private void AddRandomPoolSlot()
    {
        var s = new RandomPoolSlot { Label = "랜덤", MinCount = 1, MaxCount = 1 };
        Slots.Add(s);
        SelectedSlot = s;
        _main.Status = "랜덤 풀 슬롯 추가됨 — 오른쪽에서 풀을 고르세요";
    }

    /// <summary>"방법 A vs 방법 B" 대안 슬롯 추가. 기본 그룹 2개로 시작(빈 채로 두면 검증에서
    /// 막히니 이름만 다른 빈 그룹을 준비 — 사용자가 각 그룹에 태그를 채워 넣는다).</summary>
    [RelayCommand]
    private void AddAlternativeSlot()
    {
        var s = new AlternativeSlot { Label = "대안" };
        s.Groups.Add(new AlternativeGroup { Label = "방법 A" });
        s.Groups.Add(new AlternativeGroup { Label = "방법 B" });
        Slots.Add(s);
        SelectedSlot = s;
        _main.Status = "대안 슬롯 추가됨 — 각 그룹에 태그를 넣으세요(매 줄 그룹 하나만 통째로 뽑힘)";
    }

    /// <summary>대안 슬롯에 그룹을 하나 더 추가한다. 리스트에 여러 대안 슬롯이 있을 수 있어
    /// SelectedSlot이 아니라 클릭된 슬롯을 코드비하인드에서 직접 넘겨받는다(잘못된 슬롯에
    /// 그룹이 추가되는 걸 막기 위함).</summary>
    public void AddAlternativeGroup(AlternativeSlot slot)
    {
        slot.Groups.Add(new AlternativeGroup { Label = $"방법 {(char)('A' + slot.Groups.Count)}" });
        _main.Status = "그룹 추가됨";
    }

    /// <summary>대안 그룹 하나를 통째로 제거한다(그 안의 태그도 함께).</summary>
    public void RemoveAlternativeGroup(AlternativeSlot slot, AlternativeGroup group)
    {
        int index = slot.Groups.IndexOf(group);
        slot.Groups.Remove(group);
        Record($"그룹 '{group.Label}' 삭제",
            () => slot.Groups.Insert(Math.Min(Math.Max(index, 0), slot.Groups.Count), group));
        RefreshConflicts();
        _main.Status = "그룹 삭제됨 — 되돌리려면 ↶(Ctrl+Z)";
    }

    /// <summary>대안 그룹 안의 태그 하나 제거(코드비하인드의 칩 클릭이 호출). 슬롯 태그와
    /// 달리 그룹 단위라 전용 경로가 필요하다.</summary>
    public void RemoveTagFromGroup(AlternativeGroup group, string tag)
    {
        int index = group.Tags.IndexOf(tag);
        if (index < 0) return;
        group.Tags.RemoveAt(index);
        Record($"태그 '{tag}' 삭제", () => group.Tags.Insert(Math.Min(index, group.Tags.Count), tag));
        RefreshConflicts();
        _main.Status = $"'{tag}' 제거됨 — 되돌리려면 ↶(Ctrl+Z)";
    }

    /// <summary>대안 그룹 하나에 태그를 추가한다(콤마로 여러 개 가능). Tags 컬렉션 변경은
    /// 생성자에서 건 구독(SubscribeGroup→OnSlotTagsChanged)이 이미 RefreshConflicts를 자동
    /// 호출하므로 여기서 따로 부르지 않는다 — 저빈도 경고만 직접 붙인다.</summary>
    public void AddTagsToGroup(AlternativeGroup group, string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        var lowFreq = "";
        foreach (var piece in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (group.Tags.Contains(piece)) continue;
            group.Tags.Add(piece);
            lowFreq += LowFrequencySuffix(piece);
        }
        if (lowFreq.Length > 0) _main.Status = $"'{group.Label}'에 태그 추가됨" + lowFreq;
    }

    /// <summary>태그 빈도가 낮으면(guide.md의 저빈도 경계값 2000건 미만) 상태 표시줄에 덧붙일
    /// 경고 문구. 태그 DB가 아직 없거나(TagInfo null) 등록 안 된 태그(자연어 문구 등)면 빈 문자열.
    /// <c>pinwheel</c>·<c>tramp_stamp</c>처럼 이름만 봐서는 함정인 줄 모르는 저빈도 태그를
    /// 태그를 넣는 순간 바로 알려준다(예전엔 파이썬으로 danbooru.csv를 직접 조회해야 알았다).</summary>
    private string LowFrequencySuffix(string tag)
    {
        const int threshold = 2000;
        var postCount = TagInfo?.Lookup(tag)?.PostCount;
        return postCount is > 0 and < threshold
            ? $" ⚠ 저빈도({postCount:N0}건) — 이 슬롯에 다른 고빈도 태그가 있는지 확인하세요"
            : "";
    }

    [RelayCommand]
    private void RemoveSlot()
    {
        if (SelectedSlot != null)
        {
            var slot = SelectedSlot;
            int index = Slots.IndexOf(slot);
            Slots.Remove(slot);
            // 슬롯 객체를 그대로 들고 있으므로 되돌리면 안의 태그까지 통째로 살아난다.
            Record($"슬롯 '{SlotName(slot)}' 삭제", () =>
            {
                Slots.Insert(Math.Min(index, Slots.Count), slot);
                SelectedSlot = slot;
            });
            RefreshConflicts();
            _main.Status = "슬롯 삭제됨 — 되돌리려면 ↶(Ctrl+Z)";
        }
    }

    /// <summary>라벨이 비어 있으면 종류 이름으로 대신 부른다(되돌리기 안내 문구용).</summary>
    private static string SlotName(Slot slot) =>
        !string.IsNullOrWhiteSpace(slot.Label) ? slot.Label
        : slot switch { FixedSlot => "고정", RandomPoolSlot => "랜덤", AlternativeSlot => "대안", _ => "슬롯" };

    [RelayCommand]
    private void MoveUp()
    {
        int i = SelectedSlot == null ? -1 : Slots.IndexOf(SelectedSlot);
        if (i > 0) Slots.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        int i = SelectedSlot == null ? -1 : Slots.IndexOf(SelectedSlot);
        if (i >= 0 && i < Slots.Count - 1) Slots.Move(i, i + 1);
    }
}
