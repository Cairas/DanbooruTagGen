using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Tags;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>위저드 결과 목록의 체크 가능한 태그 한 항목.</summary>
public sealed partial class WizardTagItem : ObservableObject
{
    public WizardTagItem(Tag tag) => Tag = tag;
    public Tag Tag { get; }
    [ObservableProperty] private bool _isChecked;
    public string Display => Tag.Name.Replace('_', ' ') + (Tag.IsNsfw ? " 🔞" : "") + $" ({Tag.PostCount:N0})";
    public string Tooltip => string.IsNullOrWhiteSpace(Tag.Description) ? Tag.Name : Tag.Description;
}

/// <summary>카테고리(그룹 키워드) 단위 묶음 — 위저드 결과를 카테고리별 섹션으로 보여준다.</summary>
public sealed class WizardGroup
{
    public WizardGroup(string name, IEnumerable<WizardTagItem> items)
    {
        Name = name;
        Items = new ObservableCollection<WizardTagItem>(items);
    }
    public string Name { get; }
    public ObservableCollection<WizardTagItem> Items { get; }
}

/// <summary>컨셉 빌더 위저드: 테마 키워드(한국어/영어) → 설명·별칭까지 뒤져 관련 태그를
/// 카테고리별로 제시 → 체크한 태그로 레시피 빌더의 슬롯 구성을 만들어준다.
/// 카탈로그(문서)에 없는 컨셉을 앱 안에서 조립하는 도구.</summary>
public sealed partial class ConceptWizardViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TagDatabase _db;

    [ObservableProperty] private string _keyword = "";
    /// <summary>거의 모든 컨셉에 유효한 기본 변주 축(구도·감정)을 결과 레시피에 자동으로 붙일지.</summary>
    [ObservableProperty] private bool _includeBaseAxes = true;
    [ObservableProperty] private string _statusText = "테마 키워드를 입력하거나 아래 추천 키워드를 눌러보세요.";

    public ObservableCollection<WizardGroup> Groups { get; } = new();

    /// <summary>빈 화면에서 뭘 입력할지 막막하지 않도록 바로 눌러볼 수 있는 추천 키워드.
    /// 한국어 설명 검색(SearchTheme)에 잘 걸리는, 결과가 풍부한 테마 위주로 선정.</summary>
    public IReadOnlyList<string> SuggestedKeywords { get; } = new[]
    {
        "비", "밤", "온천", "축제", "해변", "겨울", "벚꽃", "카페",
        "메이드", "판타지", "마법", "거리", "침실", "목욕", "란제리", "촉수",
    };

    /// <summary>추천 키워드 칩 클릭 → 그 키워드로 즉시 검색.</summary>
    [RelayCommand]
    private void UseSuggestion(string keyword)
    {
        Keyword = keyword;
        Search();
    }

    /// <summary>레시피 생성이 끝나면 창을 닫아 달라는 신호(창 코드비하인드가 구독).</summary>
    public event Action? RequestClose;

    // 카테고리 섹션 표시 순서: 프롬프트 표준 순서(TagOrdering)와 유사하게.
    private static readonly string[] GroupOrder =
    {
        "인원", "신체", "머리", "눈", "표정", "의상", "노출", "포즈", "행위", "체위",
        "분비물", "도구", "소품", "시점", "배경", "형식", "품질",
    };

    public ConceptWizardViewModel(MainViewModel main, TagDatabase db)
    {
        _main = main;
        _db = db;
    }

    [RelayCommand]
    private void Search()
    {
        Groups.Clear();
        if (string.IsNullOrWhiteSpace(Keyword))
        {
            StatusText = "키워드를 입력하세요.";
            return;
        }

        var hits = _db.SearchTheme(Keyword.Trim(), 400);
        if (hits.Count == 0)
        {
            StatusText = $"'{Keyword}' 관련 태그를 찾지 못했습니다. 다른 단어로 시도해보세요.";
            return;
        }

        // 그룹 키워드(큐레이션 별칭에서 역추출)별로 묶는다. 미분류는 "기타"로.
        var byGroup = hits
            .GroupBy(t => TagGroupKeywords.FromAliases(t.Aliases) ?? "기타")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var name in GroupOrder)
            if (byGroup.TryGetValue(name, out var tags))
                Groups.Add(new WizardGroup(name, tags.Take(30).Select(t => new WizardTagItem(t))));
        if (byGroup.TryGetValue("기타", out var etc))
            Groups.Add(new WizardGroup("기타", etc.Take(30).Select(t => new WizardTagItem(t))));

        StatusText = $"'{Keyword}' 관련 {hits.Count}개 태그를 찾았습니다. 쓸 것만 체크하고 아래 버튼을 누르세요.";
    }

    /// <summary>체크된 태그로 레시피 빌더의 슬롯 구성을 만든다(현재 구성은 교체 — 빌더가 창을
    /// 닫을 때 last-recipe.json에 자동 저장되므로 직전 작업이 완전히 사라지진 않는다).
    /// 카테고리마다 [랜덤] 슬롯 하나(매 줄 1개 추출)로 넣어 컨셉 내부 변주가 자동으로 생긴다.</summary>
    [RelayCommand]
    private void CreateRecipe()
    {
        var checkedByGroup = Groups
            .Select(g => (g.Name, Tags: g.Items.Where(i => i.IsChecked).Select(i => i.Tag.Name).ToList()))
            .Where(x => x.Tags.Count > 0)
            .ToList();
        if (checkedByGroup.Count == 0)
        {
            StatusText = "체크된 태그가 없습니다. 컨셉에 쓸 태그를 먼저 체크하세요.";
            return;
        }

        var builder = _main.RecipeBuilder;
        builder.RecordSlotsBeforeReplace("컨셉 빌더로 새 구성 만들기");
        builder.Slots.Clear();

        var baseSlot = new FixedSlot { Label = "기본" };
        baseSlot.Tags.Add("1girl");
        baseSlot.Tags.Add("solo");
        builder.Slots.Add(baseSlot);

        foreach (var (name, tags) in checkedByGroup)
        {
            var slot = new RandomPoolSlot { Label = name, MinCount = 1, MaxCount = 1 };
            foreach (var t in tags) slot.Tags.Add(t);
            builder.Slots.Add(slot);
        }

        if (IncludeBaseAxes)
        {
            AttachAxis("[축] 구도·카메라", "구도");
            AttachAxis("[축] 감정·표정", "감정");
        }

        builder.SelectedSlot = builder.Slots.FirstOrDefault();
        builder.RefreshConflicts();
        _main.Status = $"컨셉 '{Keyword}' → 레시피 빌더에 {builder.Slots.Count}개 슬롯 생성됨. " +
                       "레시피 라이브러리의 '+현재 상태'로 저장해두세요.";
        RequestClose?.Invoke();

        // 이름으로 축 풀을 찾아 연결한다(사용자가 그 풀을 지웠으면 조용히 생략).
        void AttachAxis(string poolName, string label)
        {
            var pool = _main.Pools.FirstOrDefault(p => p.Name == poolName);
            if (pool == null) return;
            builder.Slots.Add(new RandomPoolSlot { Label = label, PoolId = pool.Id, MinCount = 1, MaxCount = 1 });
        }
    }
}
