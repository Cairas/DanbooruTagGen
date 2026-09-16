namespace DanbooruTagGen.Core.Models;

/// <summary>훅을 레시피의 어디에 끼울지. 레시피마다 슬롯 수가 달라 절대 인덱스만으로는
/// 부족하다 — 실제 쓰임새의 대부분은 "무조건 맨 앞" 또는 "무조건 맨 뒤"다.</summary>
public enum HookPlacement
{
    Front,
    Back,
    AtIndex,
}

/// <summary>훅이 만들어낼 슬롯의 종류. 대안(Alternative)은 지원하지 않는다 — 훅의 쓰임새는
/// "항상 붙이는 태그" 아니면 "매 줄 몇 개 뽑는 축" 둘 중 하나다.</summary>
public enum HookSlotKind
{
    Fixed,
    Random,
}

/// <summary>생성 직전에 레시피에 끼워 넣는 사용자 정의 슬롯. 레시피 자체는 건드리지 않고
/// 생성할 때만 합쳐지므로, "이번 출력에만 광원 축을 하나 더 얹고 싶다" 같은 일회성 변형을
/// 레시피를 복제해 고치지 않고도 할 수 있다.</summary>
public sealed class GenerationHook
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>꺼두면 설정은 남기고 생성에서만 제외한다.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>훅이 여러 개일 때 같은 위치를 노리는 훅끼리의 앞뒤를 정한다(작을수록 앞).</summary>
    public int Order { get; set; }

    public HookPlacement Placement { get; set; } = HookPlacement.Front;

    /// <summary><see cref="HookPlacement.AtIndex"/>일 때만 의미가 있다. 슬롯 수를 넘으면 맨 뒤로 보정된다.</summary>
    public int Index { get; set; }

    public HookSlotKind Kind { get; set; } = HookSlotKind.Random;

    /// <summary>풀 라이브러리의 풀을 참조한다. 비어 있으면 참조 없음.</summary>
    public string PoolId { get; set; } = "";

    /// <summary>훅에서 직접 입력한 태그. 참조 풀의 후보와 합쳐져 하나의 후보군이 된다
    /// (<see cref="RandomPoolSlot"/>의 기존 의미 그대로).</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary><see cref="HookSlotKind.Random"/>일 때 매 줄 뽑을 개수.</summary>
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
}
