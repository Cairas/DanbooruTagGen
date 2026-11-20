namespace DanbooruTagGen.Core.Models;

/// <summary>랜덤 슬롯의 후보 추첨 방식.
/// Uniform: 후보를 동등 확률로 추첨(기존 동작).
/// Weighted: danbooru 빈도(post count)의 제곱근에 비례해 추첨 — 인기 태그가 더 자주
/// 나오되, 제곱근으로 완만하게 눌러 희귀 태그도 살아남게 한다.</summary>
public enum SamplingMode
{
    Uniform = 0,
    Weighted = 1,
}

/// <summary>생성 옵션. 레코드인 이유: 일괄 생성이 레시피마다 줄 수·시드만 바꾼 사본을 만드는데(<c>with</c>),
/// 필드를 손으로 옮겨 적으면 옵션을 추가할 때 사본 쪽에서 빠뜨리기 쉽다.</summary>
public sealed record GenerationOptions
{
    public int LineCount { get; set; } = 100;
    public int? Seed { get; set; }
    public bool DedupeWithinLine { get; set; } = true;
    public bool AvoidDuplicateLines { get; set; } = true;
    /// <summary>모순(상호배타) 태그가 한 줄에 같이 나오면 재추첨해 피한다. 기본 켜짐.</summary>
    public bool AvoidConflicts { get; set; } = true;
    /// <summary>모순 회피를 "줄 전체 재추첨" 전에 슬롯 단위로 먼저 한다: 슬롯을 정의 순서대로 뽑으면서,
    /// 이미 확정된 앞쪽 슬롯들의 태그와 새로 충돌할 후보(랜덤 풀 후보·ALT 그룹)를 그 자리에서 빼고 뽑는다.
    /// <para>왜: 줄 전체 재추첨은 충돌과 무관한 슬롯까지 전부 다시 굴려서, 충돌 멤버를 품은 ALT 브랜치가
    /// 명목 가중치보다 훨씬 덜 나온다(n517: 14.3% → 2.3%). 로컬 필터는 앞쪽 슬롯의 선택을 존중하고 뒤쪽
    /// 슬롯만 맞춰 준다 — 그래서 "포즈 ALT가 손짓 풀보다 먼저 온다" 같은 슬롯 순서가 곧 우선순위다.</para>
    /// <para>후보가 전부 걸러지면(또는 MinCount를 못 채우면) 필터 없이 뽑고 기존 줄 재추첨에 맡긴다(폴백).
    /// <see cref="AvoidConflicts"/>가 꺼져 있으면 아무 일도 안 한다. 기본 꺼짐 — 켜면 같은 시드에서도 줄이 달라진다.</para></summary>
    public bool LocalConflictFiltering { get; set; }
    /// <summary>출력 시 '_'를 공백으로 (SD 프롬프트 관례: long_hair -> long hair). 기본 켜짐.</summary>
    public bool UnderscoreToSpace { get; set; } = true;
    /// <summary>랜덤 슬롯 추첨 방식. 기본 Uniform(기존 동작 보존). 빈도 메타데이터가
    /// 주입되지 않으면 Weighted여도 Uniform으로 폴백한다.</summary>
    public SamplingMode Sampling { get; set; } = SamplingMode.Uniform;
    /// <summary>한 줄의 태그를 표준 프롬프트 순서(품질→인원→…→배경→형식)로 재배치한다.
    /// 기본 꺼짐. 그룹/카테고리 메타데이터가 주입되지 않으면 무시된다.</summary>
    public bool AutoOrderTags { get; set; }
    public List<string> Blocklist { get; set; } = new();
    /// <summary>줄마다 "각 슬롯이 무엇을 냈는가"(<see cref="Generation.LineOrigin"/>)를 결과에 담는다. 앱의 줄 출처 보기용.
    /// 난수 소비는 꺼져 있을 때와 같다(기록만 한다) — 켜도 같은 시드면 같은 줄이 나온다. 기본 꺼짐(할당을 아끼려고).</summary>
    public bool CaptureOrigins { get; set; }
    /// <summary>이미 쓰기로 정해진 줄들의 중복 판정 키(<see cref="Generation.GenerationResult.LineKeys"/>). 줄 중복 회피가
    /// 켜져 있으면 이 줄들과 같은 줄도 피한다 — 잠근 줄을 남기고 나머지만 다시 뽑을 때 잠근 줄이 또 나오지 않게.</summary>
    public IReadOnlyCollection<string>? ReservedLineKeys { get; set; }
    /// <summary>줄 중복 회피가 켜져 있을 때, 서로 다른 줄이 바닥나면(재추첨 한도를 다 써도 중복인 줄이 연달아 나오면) 요청 줄 수를
    /// 못 채워도 거기서 멈춘다 — 중복 줄을 넣지 않고, 남은 줄마다 재추첨 한도를 다 쓰며 느려지지도 않는다.
    /// 끄면 예전처럼 중복 줄을 넣어 요청 줄 수를 맞춘다. 바닥나기 전까지는 켜든 끄든 같은 줄이 나온다.</summary>
    public bool StopWhenExhausted { get; set; }
}
