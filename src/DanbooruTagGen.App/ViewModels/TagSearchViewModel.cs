using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Tags;

namespace DanbooruTagGen.App.ViewModels;

public sealed partial class TagSearchViewModel : ObservableObject
{
    private readonly TagDatabase _db;
    private readonly Settings _settings;

    /// <summary>"⭐ 즐겨찾기" 가상 카테고리의 키워드. 큐레이션 그룹 키워드와 겹치지 않는 값.</summary>
    internal const string FavoritesKeyword = "__favorites__";

    // 현재 카테고리 모드: null=검색어 모드, ""=전체, 그 외=그룹 키워드.
    private string? _activeCategory = "";

    /// <summary>카테고리 버튼 하이라이트용으로 노출한 읽기 전용 뷰. 17개 버튼이 나열돼 있으면
    /// 지금 뭐가 활성인지 눈에 안 띄는 문제가 있었다 — XAML에서 이 값과 각 버튼의 Keyword를
    /// 비교해 배경색을 바꾼다. 검색어 모드(null)면 카테고리 버튼 전부 비활성으로 보인다.</summary>
    public string? ActiveCategoryKeyword => _activeCategory;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private Tag? _selectedTag;
    [ObservableProperty] private NsfwFilterMode _nsfwFilter = NsfwFilterMode.All;
    /// <summary>"추가" 버튼을 눌렀는데 선택된 태그가 없을 때의 안내. 예전엔 아무 메시지 없이
    /// 조용히 아무 일도 안 일어나서 "태그가 안 들어간다"로 오해하기 쉬웠다.</summary>
    [ObservableProperty] private string _addHint = "";

    public ObservableCollection<Tag> Results { get; } = new();

    /// <summary>현재 활성 슬롯/풀로 태그를 보낼 콜백(RecipeBuilder/PoolLibrary가 연결).</summary>
    public Action<string>? OnAddTag { get; set; }

    public TagSearchViewModel(TagDatabase db, Settings settings)
    {
        _db = db;
        _settings = settings;
        Refresh(); // 시작 시 인기순 상위(전체) 표시
    }

    partial void OnQueryChanged(string value)
    {
        // 사용자가 타이핑하면 검색어 모드로. (카테고리 버튼이 Query를 ""로 비울 때는 모드를 바꾸지 않음)
        if (!string.IsNullOrWhiteSpace(value))
        {
            _activeCategory = null;
            OnPropertyChanged(nameof(ActiveCategoryKeyword));
        }
        Refresh();
    }

    partial void OnNsfwFilterChanged(NsfwFilterMode value) => Refresh();

    /// <summary>활성 카테고리/검색어 + 성인 필터에 맞춰 결과 목록을 다시 채운다.</summary>
    private void Refresh()
    {
        Results.Clear();
        IEnumerable<Tag> seq;
        if (_activeCategory == FavoritesKeyword)
            seq = ApplyNsfw(FavoriteResults());                        // ⭐ 즐겨찾기 + 필터
        else if (_activeCategory != null && _activeCategory.Length == 0)
            seq = _db.TopByPostCount(500, NsfwPredicate());            // 전체(인기순) + 필터
        else if (_activeCategory != null)
            seq = ApplyNsfw(_db.Autocomplete(_activeCategory, 1000));  // 그룹 키워드 + 필터
        else if (!string.IsNullOrWhiteSpace(Query))
            seq = ApplyNsfw(_db.SearchRanked(Query, 300));            // 검색어: 정확→접두→포함 순 + 필터
        else
            seq = _db.TopByPostCount(500, NsfwPredicate());            // 빈 검색 = 전체

        foreach (var t in seq.Take(500)) Results.Add(t);
    }

    /// <summary>즐겨찾기 목록을 등록 순서 그대로 태그로 변환한다(빈도순 재정렬 안 함 —
    /// 사용자가 넣은 순서 자체가 개인화된 순서이므로). DB에서 사라진 이름은 건너뛴다.</summary>
    private IEnumerable<Tag> FavoriteResults()
    {
        foreach (var name in _settings.FavoriteTags)
            if (_db.Lookup(name) is { } tag)
                yield return tag;
    }

    /// <summary>선택한 태그를 즐겨찾기에 추가/제거한다(⭐ 버튼). 즉시 디스크에 저장돼
    /// 재시작해도 유지된다.</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedTag == null)
        {
            AddHint = "즐겨찾기할 태그를 먼저 목록에서 선택하세요.";
            return;
        }
        var name = SelectedTag.Name;
        if (_settings.FavoriteTags.Remove(name))
            AddHint = $"⭐ 해제: {name}";
        else
        {
            _settings.FavoriteTags.Add(name);
            AddHint = $"⭐ 즐겨찾기 추가: {name}";
        }
        SettingsStore.Save(_settings);
        if (_activeCategory == FavoritesKeyword) Refresh();
    }

    private Func<Tag, bool>? NsfwPredicate() => NsfwFilter switch
    {
        NsfwFilterMode.SfwOnly => t => !t.IsNsfw,
        NsfwFilterMode.NsfwOnly => t => t.IsNsfw,
        _ => null,
    };

    private IEnumerable<Tag> ApplyNsfw(IEnumerable<Tag> tags)
    {
        var p = NsfwPredicate();
        return p == null ? tags : tags.Where(p);
    }

    /// <summary>일반/성인 필터 버튼.</summary>
    [RelayCommand]
    private void SelectNsfwFilter(string mode)
    {
        NsfwFilter = mode switch
        {
            "sfw" => NsfwFilterMode.SfwOnly,
            "nsfw" => NsfwFilterMode.NsfwOnly,
            _ => NsfwFilterMode.All,
        };
    }

    [RelayCommand]
    private void AddSelected()
    {
        if (SelectedTag == null)
        {
            AddHint = "추가할 태그를 먼저 목록에서 선택(클릭)하세요.";
            return;
        }
        AddHint = "";
        OnAddTag?.Invoke(SelectedTag.Name);
    }

    /// <summary>상단 카테고리 버튼들. Keyword는 큐레이션 파일의 그룹 키워드(별칭)와 일치.</summary>
    public IReadOnlyList<CategoryButton> Categories { get; } = new[]
    {
        new CategoryButton("전체", ""),
        new CategoryButton("⭐ 즐겨찾기", FavoritesKeyword),
        new CategoryButton("인원", "인원"),
        new CategoryButton("표정", "표정"),
        new CategoryButton("눈", "눈"),
        new CategoryButton("머리", "머리"),
        new CategoryButton("포즈", "포즈"),
        new CategoryButton("시점", "시점"),
        new CategoryButton("의상", "의상"),
        new CategoryButton("소품", "소품"),
        new CategoryButton("신체", "신체"),
        new CategoryButton("배경", "배경"),
        new CategoryButton("형식", "형식"),
        new CategoryButton("노출", "노출"),
        new CategoryButton("행위", "행위"),
        new CategoryButton("체위", "체위"),
        new CategoryButton("분비물", "분비물"),
        new CategoryButton("도구", "도구"),
        new CategoryButton("품질", "품질"),
    };

    /// <summary>카테고리 버튼 클릭 → 그 그룹 키워드로(전체는 빈 키워드). 성인 필터와 함께 적용된다.</summary>
    [RelayCommand]
    private void SelectCategory(string keyword)
    {
        _activeCategory = keyword ?? "";   // "" = 전체
        OnPropertyChanged(nameof(ActiveCategoryKeyword));
        if (Query.Length > 0) Query = "";  // setter가 OnQueryChanged→Refresh (빈 값이라 모드는 유지)
        else Refresh();
    }
}

/// <summary>상단 카테고리 버튼 한 개(표시 라벨 + 검색 그룹 키워드).</summary>
public sealed record CategoryButton(string Label, string Keyword);

/// <summary>검색 결과의 성인 필터 모드.</summary>
public enum NsfwFilterMode { All, SfwOnly, NsfwOnly }
