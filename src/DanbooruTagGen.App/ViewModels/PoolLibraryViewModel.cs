using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>풀 후보 한 항목의 표시용 뷰. Pool.Candidates는 생성기가 쓰는 순수 태그명
/// 문자열이라 설명/카테고리 정보가 없다 — 태그 DB에서 조회해 여기 채운다. DB에 없는
/// 태그(예: 사용자가 직접 타이핑한 값)는 이름만 있고 나머지가 빈 상태로 최소 표시된다.</summary>
public sealed record PoolCandidateView(string Name, string Description, string CategoryLabel, int PostCount, string AliasesText, bool IsNsfw);

public sealed partial class PoolLibraryViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private Pool? _selectedPool;
    [ObservableProperty] private PoolCandidateView? _selectedCandidate;
    public ObservableCollection<Pool> Pools { get; }
    /// <summary>SelectedPool.Candidates(문자열)를 태그 DB로 보강한 표시용 목록. 호버 툴팁이 여길 바라본다.</summary>
    public ObservableCollection<PoolCandidateView> SelectedPoolCandidates { get; } = new();

    public PoolLibraryViewModel(MainViewModel main)
    {
        _main = main;
        Pools = new ObservableCollection<Pool>(main.Pools);
    }

    /// <summary>선택한 풀을 쓰는 레시피 목록 안내("이 풀을 지우면 무엇이 깨지는가").
    /// 풀 공유가 이 프로그램의 핵심 구조인데 영향 범위가 화면에 전혀 안 보였다.</summary>
    [ObservableProperty] private string _selectedPoolUsage = "";

    partial void OnSelectedPoolChanged(Pool? value)
    {
        RefreshSelectedPoolCandidates();
        RefreshSelectedPoolUsage();
    }

    private void RefreshSelectedPoolUsage()
    {
        if (SelectedPool == null) { SelectedPoolUsage = ""; return; }
        var users = PoolReferences.FindRecipesUsing(SelectedPool.Id, _main.SavedRecipes);
        SelectedPoolUsage = users.Count == 0
            ? "이 풀을 참조하는 레시피 없음"
            : $"레시피 {users.Count}개가 참조 중: {string.Join(", ", users.Take(6))}"
              + (users.Count > 6 ? $" …외 {users.Count - 6}개" : "");
    }

    /// <summary>선택 풀의 후보 문자열 목록을 태그 DB 조회로 다시 채운다. 풀을 바꿀 때뿐 아니라
    /// 태그를 추가/삭제한 뒤에도 호출해 툴팁 목록을 동기화한다.</summary>
    private void RefreshSelectedPoolCandidates()
    {
        SelectedPoolCandidates.Clear();
        if (SelectedPool == null) return;
        foreach (var name in SelectedPool.Candidates)
            SelectedPoolCandidates.Add(ToView(name));
    }

    private PoolCandidateView ToView(string name)
    {
        var tag = _main.TagInfo?.Lookup(name);
        return tag is null
            ? new PoolCandidateView(name, "", "", 0, "", false)
            : new PoolCandidateView(name, tag.Description, tag.Category.ToString(), tag.PostCount,
                string.Join(", ", tag.Aliases), tag.IsNsfw);
    }

    /// <summary>풀 편집 창이 활성일 때 태그 검색 결과를 선택 풀로 보낸다.</summary>
    public void AttachTagSearch() => _main.TagSearch.OnAddTag = AddTagToSelected;

    private void AddTagToSelected(string tag)
    {
        // 예전엔 풀을 선택 안 해둔 상태로 검색에서 더블클릭하면 아무 메시지 없이 조용히
        // 아무 일도 안 일어났다 — "몇몇 태그는 안 들어간다"는 문제의 실제 원인 중 하나.
        // 성공/실패 모두 상태 표시줄에 남겨 왜 안 들어갔는지 바로 보이게 한다.
        if (SelectedPool == null)
        {
            _main.Status = "태그를 추가할 풀을 먼저 왼쪽 목록에서 선택하세요.";
            return;
        }
        if (SelectedPool.Candidates.Contains(tag))
        {
            _main.Status = $"'{tag}' 은(는) 이미 풀 '{SelectedPool.Name}'에 있음";
            return;
        }
        SelectedPool.Candidates.Add(tag);
        OnPropertyChanged(nameof(SelectedPool));
        RefreshSelectedPoolCandidates();
        _main.Status = $"'{tag}' 추가됨 → 풀 '{SelectedPool.Name}' ({SelectedPool.Candidates.Count}개)";
    }

    [RelayCommand]
    private void AddPool()
    {
        var p = new Pool { Name = "새 풀" };
        Pools.Add(p);
        SelectedPool = p;
    }

    [RelayCommand]
    private void RemovePool()
    {
        // 되돌리기가 없고, 창을 닫기만 해도 자동 저장으로 확정된다 — 공들여 만든 풀이
        // 실수로 사라지는 걸 막는 마지막 방어선으로 확인창을 둔다.
        if (SelectedPool == null) return;
        var name = SelectedPool.Name;
        var count = SelectedPool.Candidates.Count;

        // 참조하는 레시피가 있으면 그 레시피들은 다음 생성 때 "참조하는 풀을 찾을 수 없습니다"로
        // 막힌다. 지우기 전에 무엇이 깨지는지 눈으로 보여 준다.
        var users = PoolReferences.FindRecipesUsing(SelectedPool.Id, _main.SavedRecipes);
        var warning = users.Count == 0
            ? ""
            : $"\n\n⚠ 이 풀을 참조하는 레시피 {users.Count}개가 생성 불가 상태가 됩니다:\n· "
              + string.Join("\n· ", users.Take(10))
              + (users.Count > 10 ? $"\n· …외 {users.Count - 10}개" : "");

        var confirm = System.Windows.MessageBox.Show(
            $"풀 '{name}' ({count}개 태그)를 삭제할까요? 되돌릴 수 없습니다.{warning}",
            "풀 삭제", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        Pools.Remove(SelectedPool);
        _main.Status = $"풀 '{name}' 삭제됨";
    }

    [RelayCommand]
    private void RemoveSelectedTag()
    {
        if (SelectedPool != null && SelectedCandidate != null)
            SelectedPool.Candidates.Remove(SelectedCandidate.Name);
        OnPropertyChanged(nameof(SelectedPool));
        RefreshSelectedPoolCandidates();
    }

    [RelayCommand]
    private void Save() => Persist();

    /// <summary>Pools 목록을 MainViewModel과 디스크에 반영. 저장 버튼뿐 아니라 라이브러리 창을
    /// 닫을 때도 호출돼(MainViewModel.OpenPoolLibrary), 저장 버튼을 깜빡 잊어도 태그
    /// 추가/삭제나 풀 이름 변경이 사라지지 않게 하는 안전망 역할을 한다.</summary>
    public void Persist()
    {
        _main.Pools.Clear();
        _main.Pools.AddRange(Pools);
        _main.SavePools();
        _main.Status = "풀 저장됨";
    }

    /// <summary>선택한 풀을 레시피 빌더의 [랜덤] 슬롯에 연결한다. 풀 라이브러리에서
    /// 만든 풀을 쓰려면 레시피 빌더로 돌아가 슬롯을 만들고 콤보에서 이름을 찾아야 했는데,
    /// 여기서 한 번에 끝내는 지름길. 레시피 빌더에서 이미 [랜덤] 슬롯을 선택해 둔 상태면
    /// 새 슬롯을 만들지 않고 그 슬롯의 풀만 바꿔치기한다 — 매번 새 슬롯이 생기면 풀을
    /// 갈아 끼울 때마다 슬롯이 계속 늘어나 헷갈리기 때문.</summary>
    [RelayCommand]
    private void AddAsRandomSlot()
    {
        if (SelectedPool == null)
        {
            _main.Status = "레시피에 추가할 풀을 먼저 선택하세요.";
            return;
        }
        if (_main.RecipeBuilder.SelectedSlot is RandomPoolSlot existing)
        {
            existing.PoolId = SelectedPool.Id;
            _main.Status = $"[랜덤] {existing.Label} → 풀 '{SelectedPool.Name}'로 교체됨";
            return;
        }
        var slot = new RandomPoolSlot { Label = SelectedPool.Name, PoolId = SelectedPool.Id, MinCount = 1, MaxCount = 1 };
        _main.RecipeBuilder.Slots.Add(slot);
        _main.RecipeBuilder.SelectedSlot = slot;
        _main.Status = $"풀 '{SelectedPool.Name}' → 레시피에 새 랜덤 슬롯으로 추가됨";
    }

    /// <summary>선택한 풀의 태그를 그대로 복사해 레시피 빌더에 새 [고정] 슬롯으로 넣는다
    /// (매 줄 전부 출력 — 랜덤 슬롯과 달리 일부만 뽑지 않음). 고정 슬롯은 풀을 참조하지 않고
    /// 스냅샷만 뜨므로, 나중에 풀 내용을 바꿔도 이 슬롯엔 반영되지 않는다.</summary>
    [RelayCommand]
    private void AddAsFixedSlot()
    {
        if (SelectedPool == null)
        {
            _main.Status = "레시피에 추가할 풀을 먼저 선택하세요.";
            return;
        }
        var slot = new FixedSlot { Label = SelectedPool.Name };
        foreach (var tag in SelectedPool.Candidates) slot.Tags.Add(tag);
        _main.RecipeBuilder.Slots.Add(slot);
        _main.RecipeBuilder.SelectedSlot = slot;
        _main.Status = $"풀 '{SelectedPool.Name}' → 레시피에 새 고정 슬롯으로 추가됨 ({slot.Tags.Count}개, 항상 전부 출력)";
    }
}
