namespace DanbooruTagGen.Core.Generation;

/// <summary>충돌이 검출된 한 줄(0-기반 줄 번호 + 검출 내역).</summary>
public sealed record LineConflict(int LineIndex, IReadOnlyList<ConflictHit> Hits);

public sealed record GenerationResult(
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>최종 출력에 남은 모순 줄들(충돌 회피 후에도 못 피한 줄 포함). 기본 빈 목록.</summary>
    public IReadOnlyList<LineConflict> Conflicts { get; init; } = System.Array.Empty<LineConflict>();
}
