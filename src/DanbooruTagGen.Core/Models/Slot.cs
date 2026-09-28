using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DanbooruTagGen.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FixedSlot), "fixed")]
[JsonDerivedType(typeof(RandomPoolSlot), "randomPool")]
[JsonDerivedType(typeof(AlternativeSlot), "alternative")]
public abstract class Slot : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // PoolId 같은 단순 문자열/숫자 속성은 ObservableCollection과 달리 값 대입만으로는
    // WPF에 변경을 알리지 않는다(예: 풀 콤보 선택 해제 버튼이 코드비하인드에서 직접 대입).
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _label = "";
    /// <summary>가독성용 라벨(예: "표정", "배경"). 출력에는 포함되지 않음.</summary>
    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    private bool _isEditingLabel;
    /// <summary>레시피 빌더에서 라벨을 더블클릭/F2로 인라인 편집 중인지(순수 UI 상태).
    /// 저장할 값이 아니라서 JSON에는 남기지 않는다.</summary>
    [JsonIgnore]
    public bool IsEditingLabel
    {
        get => _isEditingLabel;
        set { _isEditingLabel = value; OnPropertyChanged(); }
    }

    private bool _isEnabled = true;
    /// <summary>꺼두면(false) 슬롯을 지우지 않고도 생성에서 제외한다(WildcardGenerator가 건너뜀).
    /// 삭제/재입력 없이 잠깐 껐다 켤 수 있게 하는 토글 — 레시피에 그대로 남으므로 저장/불러오기도 유지된다.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }
}

/// <summary>항상 순서대로 출력되는 고정 태그들.</summary>
public sealed class FixedSlot : Slot
{
    // ObservableCollection: UI가 태그 추가/삭제를 즉시 반영하도록(WPF 바인딩 통지).
    public ObservableCollection<string> Tags { get; set; } = new();
}

/// <summary>후보 태그들에서 매 줄 Min~Max개를 비복원 추출.
/// 후보는 이 슬롯에 직접 넣은 <see cref="Tags"/>와 <see cref="PoolId"/>·<see cref="ExtraPoolIds"/>로
/// 참조하는 공용 풀(들)의 후보를 모두 합쳐서 쓴다(WildcardGenerator.ResolveCandidates).
/// 예전엔 Tags가 하나라도 있으면 PoolId를 통째로 무시했는데, 풀을 연결한 슬롯에 태그
/// 하나만 실수로 더 넣어도 나머지 풀 후보가 조용히 전부 사라지는 문제가 있어 합치는 쪽으로 바꿨다.</summary>
public sealed class RandomPoolSlot : Slot
{
    // 이 슬롯에서 직접 고른 후보 태그들(공용 풀 없이도 바로 랜덤). ObservableCollection: UI 즉시 반영.
    public ObservableCollection<string> Tags { get; set; } = new();

    private string _poolId = "";
    public string PoolId
    {
        get => _poolId;
        set { _poolId = value; OnPropertyChanged(); }
    }

    /// <summary>"풀 체이닝": PoolId 외에 이 슬롯이 추가로 함께 참조하는 풀들. 여기 나열된
    /// 풀들의 후보는 PoolId·Tags와 하나로 합쳐진 뒤 그 안에서 Min~Max개를 뽑는다 — 풀을
    /// 슬롯마다 따로 붙이면(예: 체위 풀 두 개를 별도 슬롯으로) 한 줄에 서로 다른 체위 태그가
    /// 동시에 뽑히는 모순이 생길 수 있어, 반드시 하나의 후보군으로 합쳐야 한다.</summary>
    public ObservableCollection<string> ExtraPoolIds { get; set; } = new();

    private int _minCount = 1;
    public int MinCount
    {
        get => _minCount;
        set { _minCount = value; OnPropertyChanged(); }
    }

    private int _maxCount = 1;
    public int MaxCount
    {
        get => _maxCount;
        set { _maxCount = value; OnPropertyChanged(); }
    }

    /// <summary>JSON의 <c>chancePercent</c>가 정수가 아니었음을 나타내는 표식. 정상 범위(0~100)
    /// 밖이라 <see cref="Generation.WildcardGenerator.Validate"/>가 반드시 걸러 낸다.</summary>
    public const int UnparsedChance = int.MinValue;

    private int _chancePercent = 100;
    /// <summary>이 슬롯이 한 줄에서 "발동"할 확률(0~100, 기본 100 = 항상). 발동하지 않으면 그 줄엔
    /// 0개, 발동하면 기존대로 Min~Max개를 뽑는다. MinCount=0으로는 "0개"가 균등한 한 값일 뿐이라
    /// "70%로 뜨고, 뜨면 1~6개"처럼 발동 여부와 개수를 따로 조절할 수 없어서 추가했다.
    /// <para>반드시 <c>int</c>다 — <see cref="AlternativeGroup.Weight"/>에 소수(0.3)가 들어가
    /// 역직렬화가 조용히 실패해 레시피가 통째로 시딩에서 빠진 사고가 있었다. 그래서 JSON 쪽은
    /// <see cref="ChancePercentJsonConverter"/>가 파일 로드를 깨뜨리지 않고 받아 두었다가
    /// 정수가 아니면 <see cref="UnparsedChance"/>로 표시해, 생성 전 검증에서 명시적으로 막는다.</para></summary>
    [JsonConverter(typeof(ChancePercentJsonConverter))]
    public int ChancePercent
    {
        get => _chancePercent;
        set
        {
            _chancePercent = value;
            _chancePercentText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChancePercentText));
            OnPropertyChanged(nameof(ChancePercentError));
            OnPropertyChanged(nameof(HasChancePercentError));
        }
    }

    private string? _chancePercentText;
    /// <summary>편집칸 전용(순수 UI 상태, 저장 안 함). int에 직접 바인딩하면 빈 칸을 "100"으로
    /// 받을 수 없고, 잘못 친 글자는 WPF가 조용히 버려 오류를 보여 줄 수 없어서 문자열을 거친다.
    /// 비우면 100, 정수면 그대로(범위 밖이어도 넣어 둬 생성 검증도 같이 막는다), 정수가 아니면
    /// 값은 그대로 두고 <see cref="ChancePercentError"/>만 띄운다.</summary>
    [JsonIgnore]
    public string ChancePercentText
    {
        get => _chancePercentText ?? (_chancePercent == UnparsedChance ? "" : _chancePercent.ToString());
        set
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0) ChancePercent = 100;
            else if (int.TryParse(text, out var v)) ChancePercent = v;
            _chancePercentText = text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChancePercentError));
            OnPropertyChanged(nameof(HasChancePercentError));
        }
    }

    /// <summary>편집칸 아래에 빨갛게 띄울 문구. 문제가 없으면 "".</summary>
    [JsonIgnore]
    public string ChancePercentError =>
        _chancePercentText is { Length: > 0 } t && !int.TryParse(t, out _) ? "발동 확률은 0~100 사이 정수만 입력하세요."
        : _chancePercent == UnparsedChance ? "발동 확률이 정수가 아닙니다. 0~100 사이 정수로 고치세요."
        : _chancePercent is < 0 or > 100 ? $"발동 확률 {_chancePercent}%는 0~100 범위를 벗어났습니다."
        : "";

    [JsonIgnore]
    public bool HasChancePercentError => ChancePercentError.Length > 0;
}

/// <summary>함께 나와야 하는 태그 묶음 하나(대안 그룹). AlternativeSlot이 매 줄 여러 그룹 중
/// 정확히 하나를 통째로 뽑는다 — Tags 안의 태그는 항상 같이 나오고, 다른 그룹의 태그와는
/// 절대 섞이지 않는다.</summary>
public sealed class AlternativeGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _label = "";
    /// <summary>가독성용 라벨(예: "방법 A: 목줄", "방법 B: 감방 유니폼"). 출력에는 포함 안 됨.</summary>
    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> Tags { get; set; } = new();

    private int _weight = 1;
    /// <summary>이 그룹이 뽑힐 상대 가중치(기본 1 = 다른 그룹과 동등). 0이면 절대 안 뽑힌다.
    /// <para>
    /// 왜 필요한가: 예전에는 그룹마다 확률이 균등하게 고정돼 있어서, "삽입 중"이 더 자주
    /// 나오게 하려면 같은 그룹을 기본형·<c>rough_sex</c> 추가형으로 <b>복제</b>하는 수밖에
    /// 없었다. 그 결과 한 ALT의 그룹 칸이 비중 조절에 소모돼 정작 장면의 다양성에는 쓰이지
    /// 못했다(배신·상납 4부작이 4그룹→6그룹으로 늘어난 게 이 사례). 가중치를 두면 그룹 수는
    /// 장면의 가짓수로만 쓰고, 비중은 이 값으로 따로 조절할 수 있다.
    /// </para></summary>
    public int Weight
    {
        get => _weight;
        set { _weight = value; OnPropertyChanged(); }
    }

    private string _pendingTagInput = "";
    /// <summary>이 그룹에 태그를 추가하는 입력칸의 임시 값(순수 UI 상태). 저장 안 함.</summary>
    [JsonIgnore]
    public string PendingTagInput
    {
        get => _pendingTagInput;
        set { _pendingTagInput = value; OnPropertyChanged(); }
    }
}

/// <summary>"이 컨셉을 표현하는 방법 A vs 방법 B" 같은, 서로 다른 대안이지만 하나의 컨셉
/// 정체성을 이루는 태그 묶음들. 매 줄 Groups 중 정확히 하나만 통째로 뽑혀서 그 그룹의
/// 태그가 전부 함께 나온다(다른 그룹과는 섞이지 않음) — 표정·체위 같은 그 아래 가변
/// 슬롯들과는 독립적으로 공존한다. 예: FIXED로 a·b를 둘 다 넣으면 항상 함께 나와 컨셉이
/// 뒤섞여 이상하게 그려지고, 보통 RandomPoolSlot은 개별 태그 단위로 섞어 뽑아서
/// "a의 태그 하나 + b의 태그 하나"처럼 그룹이 깨질 수 있다 — 이 슬롯은 그룹 전체를
/// 원자적으로 선택해 그 두 문제를 모두 막는다.</summary>
public sealed class AlternativeSlot : Slot
{
    public ObservableCollection<AlternativeGroup> Groups { get; set; } = new();
}
