using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>훅 하나의 편집 뷰. <see cref="GenerationHook"/> 자체를 그대로 물고 있어(복사본 아님)
/// 여기서 고치면 MainViewModel.Hooks의 값이 바로 바뀐다 — 디스크 저장은 창을 닫을 때와
/// 추가·삭제 시점에 한 번씩 한다.</summary>
public sealed partial class HookEditViewModel : ObservableObject
{
    public HookEditViewModel(GenerationHook hook, IReadOnlyList<Pool> pools)
    {
        Hook = hook;
        Pools = new ObservableCollection<Pool>(pools);
        _tagsText = string.Join(", ", hook.Tags);
    }

    public GenerationHook Hook { get; }

    /// <summary>참조 풀 후보 목록(풀 라이브러리 전체).</summary>
    public ObservableCollection<Pool> Pools { get; }

    public string Name
    {
        get => Hook.Name;
        set { Hook.Name = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => Hook.IsEnabled;
        set { Hook.IsEnabled = value; OnPropertyChanged(); }
    }

    public int Order
    {
        get => Hook.Order;
        set { Hook.Order = value; OnPropertyChanged(); }
    }

    public int Index
    {
        get => Hook.Index;
        set { Hook.Index = value; OnPropertyChanged(); }
    }

    public int MinCount
    {
        get => Hook.MinCount;
        set { Hook.MinCount = value; OnPropertyChanged(); }
    }

    public int MaxCount
    {
        get => Hook.MaxCount;
        set { Hook.MaxCount = value; OnPropertyChanged(); }
    }

    /// <summary>라디오 버튼 두 개(랜덤/고정)를 enum 하나에 묶는다. 한쪽을 켜면 다른 쪽 표시도
    /// 같이 갱신돼야 해서 두 속성이 서로를 통지한다.</summary>
    public bool IsRandom
    {
        get => Hook.Kind == HookSlotKind.Random;
        set
        {
            if (!value) return;
            Hook.Kind = HookSlotKind.Random;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFixed));
        }
    }

    public bool IsFixed
    {
        get => Hook.Kind == HookSlotKind.Fixed;
        set
        {
            if (!value) return;
            Hook.Kind = HookSlotKind.Fixed;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRandom));
        }
    }

    public bool PlaceFront
    {
        get => Hook.Placement == HookPlacement.Front;
        set { if (value) SetPlacement(HookPlacement.Front); }
    }

    public bool PlaceBack
    {
        get => Hook.Placement == HookPlacement.Back;
        set { if (value) SetPlacement(HookPlacement.Back); }
    }

    public bool PlaceAtIndex
    {
        get => Hook.Placement == HookPlacement.AtIndex;
        set { if (value) SetPlacement(HookPlacement.AtIndex); }
    }

    private void SetPlacement(HookPlacement placement)
    {
        Hook.Placement = placement;
        OnPropertyChanged(nameof(PlaceFront));
        OnPropertyChanged(nameof(PlaceBack));
        OnPropertyChanged(nameof(PlaceAtIndex));
    }

    /// <summary>선택된 참조 풀. 비우면 참조 없음(직접 입력 태그만 쓴다).</summary>
    public Pool? SelectedPool
    {
        get => Pools.FirstOrDefault(p => p.Id == Hook.PoolId);
        set { Hook.PoolId = value?.Id ?? ""; OnPropertyChanged(); }
    }

    /// <summary>직접 입력 태그를 쉼표로 편집한다. 참조 풀의 후보와 합쳐져 하나의 후보군이 된다.</summary>
    [ObservableProperty] private string _tagsText = "";

    partial void OnTagsTextChanged(string value) =>
        Hook.Tags = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    [RelayCommand]
    private void ClearPool() => SelectedPool = null;
}

/// <summary>훅 설정 창의 뷰모델. 풀·레시피 라이브러리와 같은 패턴이다.</summary>
public sealed partial class HookLibraryViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HookLibraryViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    public ObservableCollection<HookEditViewModel> Hooks { get; } = new();

    /// <summary>자동 정렬을 켜면 최종 출력이 표준 프롬프트 순서로 재배치돼 훅의 "몇 번째"가
    /// 무의미해진다. 훅 창에서만 보이면 되는 경고라 뷰모델 속성으로 둔다.</summary>
    public bool AutoOrderWarningVisible => _main.Generation?.AutoOrderTags == true;

    /// <summary>훅이 하나도 없을 때 안내를 띄우기 위한 플래그.</summary>
    public bool HasNoHooks => Hooks.Count == 0;

    public void Refresh()
    {
        Hooks.Clear();
        foreach (var hook in _main.Hooks.OrderBy(h => h.Order))
            Hooks.Add(new HookEditViewModel(hook, _main.Pools));
        OnPropertyChanged(nameof(AutoOrderWarningVisible));
        OnPropertyChanged(nameof(HasNoHooks));
    }

    [RelayCommand]
    private void AddHook()
    {
        var hook = new GenerationHook
        {
            Name = "새 훅",
            // 새 훅은 항상 기존 훅들 뒤에 붙는다 — 같은 위치를 노릴 때의 앞뒤가 예측 가능하도록.
            Order = _main.Hooks.Count == 0 ? 0 : _main.Hooks.Max(h => h.Order) + 1,
        };
        _main.Hooks.Add(hook);
        Refresh();
        Persist();
    }

    /// <summary>훅 하나를 지운다. 레시피 라이브러리의 슬롯 삭제와 달리 확인을 받는 이유는,
    /// 훅은 이름·태그·위치·개수를 전부 손으로 채워 넣는 것이라 실수로 지우면 다시 만드는 게
    /// 일이기 때문이다.</summary>
    [RelayCommand]
    private void RemoveHook(HookEditViewModel item)
    {
        var answer = System.Windows.MessageBox.Show(
            $"훅 '{item.Hook.Name}'을 삭제할까요?", "훅 삭제",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _main.Hooks.Remove(item.Hook);
        Refresh();
        Persist();
    }

    /// <summary>창을 닫을 때와 항목을 추가·삭제할 때 저장한다. 생성 패널의 훅 요약도 같이 갱신한다.</summary>
    public void Persist()
    {
        _main.SaveHooks();
        _main.Generation?.RefreshHookSummary();
    }
}
