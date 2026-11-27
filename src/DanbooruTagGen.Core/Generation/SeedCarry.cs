namespace DanbooruTagGen.Core.Generation;

/// <summary>생성 카드의 시드 정하기. 시드 칸이 자동(비었음)이면 미리보기는 매번 새 시드를 뽑고,
/// 바로 다음 [생성] 한 번은 그 미리보기 시드를 이어 쓴다 — 같은 시드면 미리보기 줄이 생성 결과 앞부분과 같으므로
/// (생성기의 앞부분 일치 성질) "미리보기로 몇 줄 보고 바로 파일로" 쓸 때 본 줄이 파일 앞쪽에 그대로 들어간다.
/// 예전엔 생성이 새 시드를 뽑아 미리보기에서 본 줄이 파일엔 없었다.</summary>
public sealed class SeedCarry
{
    private int? _fromPreview;

    /// <param name="fixedSeed">시드 칸에 적은 숫자(자동이면 null). 있으면 그대로 쓰고 이어 쓰기는 버린다.</param>
    /// <returns>쓸 시드와, 그게 직전 미리보기에서 이어 쓴 것인지.</returns>
    public (int Seed, bool Carried) Next(int? fixedSeed, bool preview, Func<int> random)
    {
        if (fixedSeed is { } s) { _fromPreview = null; return (s, false); }
        if (preview)
        {
            var seed = random();
            _fromPreview = seed;
            return (seed, false);
        }
        // 쓰기 전에 멈춘 생성(새 파일 모드 충돌·취소·검증 오류)도 있으므로 여기서 버리지 않는다 — 파일에 쓴 뒤 Written()에서.
        if (_fromPreview is { } carried) return (carried, true);
        return (random(), false);
    }

    /// <summary>생성이 파일에 썼다 — 미리보기 시드 이어 쓰기는 여기까지(다음 생성은 새 시드).</summary>
    public void Written() => _fromPreview = null;

    /// <summary>시드 칸을 바꿨다 — 이어 쓸 미리보기 시드를 버린다.</summary>
    public void Reset() => _fromPreview = null;
}
