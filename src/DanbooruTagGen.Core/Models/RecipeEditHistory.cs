namespace DanbooruTagGen.Core.Models;

/// <summary>레시피 빌더의 <b>파괴적</b> 편집만 되돌리는 스택.
///
/// 왜 필요한가: 태그 칩은 한 번 클릭하면 즉시 사라지고, 슬롯·대안 그룹 삭제와 "불러오기"의
/// 구성 전체 교체도 확인창 없이 바로 확정된다. 게다가 창을 닫기만 해도 자동 저장으로
/// 굳어져서, 공들여 만든 축 하나를 잘못 지우면 복구할 방법이 아예 없었다.
///
/// <para>되돌리는 방법을 <see cref="Action"/>으로 받는다 — 지운 슬롯·그룹 객체와 그 인덱스를
/// 클로저에 담아 두면 내용까지 그대로 제자리에 돌아온다. 태그 추가나 라벨 변경처럼 잃을 게
/// 없는 편집은 일부러 쌓지 않는다(스택이 그런 걸로 차면 정작 삭제를 못 되돌린다).</para></summary>
public sealed class RecipeEditHistory
{
    /// <summary>보관하는 최대 편집 수. 세션 안에서 "방금 그거" 몇 단계를 되돌리는 게 목적이라
    /// 깊을 필요가 없고, 지운 슬롯 객체를 계속 붙들고 있게 되므로 상한을 둔다.</summary>
    public const int MaxDepth = 20;

    private readonly LinkedList<(string Label, Action Undo)> _entries = new();

    /// <summary>되돌리는 중인지. 되돌리는 동작이 컬렉션을 건드리면 뷰모델의 삭제 훅이 다시
    /// 불리는데, 그걸 그대로 쌓으면 되돌리기와 다시하기가 서로를 먹여 스택이 안 비워진다.</summary>
    private bool _undoing;

    public bool CanUndo => _entries.Count > 0;

    /// <summary>다음 <see cref="Undo"/>가 되돌릴 편집의 이름(없으면 null). 버튼 툴팁용.</summary>
    public string? NextLabel => _entries.Last?.Value.Label;

    public void Push(string label, Action undo)
    {
        if (_undoing) return;
        _entries.AddLast((label, undo));
        if (_entries.Count > MaxDepth) _entries.RemoveFirst();
    }

    /// <summary>가장 최근 편집을 되돌리고 그 이름을 돌려준다. 되돌릴 게 없으면 null.</summary>
    public string? Undo()
    {
        var last = _entries.Last;
        if (last is null) return null;
        _entries.RemoveLast();

        _undoing = true;
        try { last.Value.Undo(); }
        finally { _undoing = false; }

        return last.Value.Label;
    }

    public void Clear() => _entries.Clear();
}
