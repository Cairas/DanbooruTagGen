namespace DanbooruTagGen.Core.Generation;

/// <summary>충돌이 검출된 한 줄(0-기반 줄 번호 + 검출 내역).</summary>
public sealed record LineConflict(int LineIndex, IReadOnlyList<ConflictHit> Hits);

public enum SlotOriginKind { Fixed, Pool, Alternative }

/// <summary>최종 채택된 줄에서 슬롯 하나가 낸 것. <see cref="Emitted"/>는 줄에 실제로 들어간 태그(가중치 범위는 치환된 값),
/// <see cref="Dropped"/>는 뽑혔지만 줄 내 중복·블록리스트로 빠진 것, <see cref="FilteredOut"/>은 슬롯 단위 로컬 필터가
/// 앞쪽 슬롯과의 모순 때문에 이번 줄 후보에서 뺀 것(ALT는 그룹 라벨, 풀은 후보 원문). <see cref="GroupLabel"/>은 ALT에서
/// 뽑힌 그룹 라벨(라벨이 비면 "그룹 N").</summary>
public sealed record SlotOrigin(
    int SlotIndex,
    string SlotLabel,
    SlotOriginKind Kind,
    string? GroupLabel,
    IReadOnlyList<string> Emitted,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> FilteredOut);

/// <summary>한 줄의 출처: 켜진 슬롯마다 하나씩(<see cref="Slots"/>, 슬롯 순서) + 이 줄을 얻기까지 줄 전체를 다시 뽑은 횟수.</summary>
public sealed record LineOrigin(IReadOnlyList<SlotOrigin> Slots, int Retries);

public sealed record GenerationResult(
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>최종 출력에 남은 모순 줄들(충돌 회피 후에도 못 피한 줄 포함). 기본 빈 목록.</summary>
    public IReadOnlyList<LineConflict> Conflicts { get; init; } = System.Array.Empty<LineConflict>();

    /// <summary>줄마다의 중복 판정 키(<see cref="Lines"/>와 같은 순서). 다음 생성의
    /// <see cref="Models.GenerationOptions.ReservedLineKeys"/>로 넘기면 그 줄들을 피한다. 생성기가 아닌 곳에서 만든 결과는 빈 목록.</summary>
    public IReadOnlyList<string> LineKeys { get; init; } = System.Array.Empty<string>();

    /// <summary><see cref="Models.GenerationOptions.CaptureOrigins"/>를 켰을 때 줄마다의 출처(<see cref="Lines"/>와 같은 순서). 끄면 null.</summary>
    public IReadOnlyList<LineOrigin>? Origins { get; init; }

    /// <summary><see cref="Models.GenerationOptions.StopWhenExhausted"/>로 요청 줄 수보다 일찍 멈췄는가(서로 다른 줄이 바닥남).</summary>
    public bool Exhausted { get; init; }
}
