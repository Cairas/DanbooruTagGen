namespace DanbooruTagGen.Core.Persistence;

/// <summary>프리셋 자동 갱신(워처)의 "읽기 실패 시 재시도" 판단. MainViewModel이 쓰고, WPF 없이
/// 단위 테스트하려고 Core로 뺐다.
/// <para>핵심 규칙: <b>파일 변경 이벤트가 올 때마다 재시도 예산을 새로 준다.</b> 레시피 JSON을
/// 여러 번 나눠 편집하다 중간에 문법이 깨진 저장이 끼면 그 이벤트의 재시도는 다 실패해 예산을
/// 소진한다. 예산을 성공 때만 되돌리면, 다음(유효한) 저장 이벤트의 첫 읽기가 하필 쓰기 잠금에
/// 걸렸을 때 재시도 없이 그대로 포기해 버린다. 변경마다 리셋하면 매 저장이 독립적으로 온전한
/// 재시도 기회를 가지므로, 앱을 재시작하지 않아도 다음 유효한 저장에서 반드시 다시 동기화된다.</para>
/// 무한 재시도는 막는다 — 같은 변경에 대해선 최대 <see cref="MaxRetries"/>번까지만.</summary>
public sealed class PresetSyncRetryGate
{
    public PresetSyncRetryGate(int maxRetries) => MaxRetries = maxRetries;

    public int MaxRetries { get; }

    /// <summary>현재 변경에 대해 이미 예약한 재시도 횟수.</summary>
    public int Retries { get; private set; }

    /// <summary>번들 파일이 바뀌었다(워처 이벤트) — 새 변경이므로 예산을 되돌린다.</summary>
    public void OnSourceChanged() => Retries = 0;

    /// <summary>읽기에 성공했다.</summary>
    public void OnSucceeded() => Retries = 0;

    /// <summary>읽기에 실패했다. 재시도를 예약해야 하면 true(횟수 1 증가), 예산을 다 썼으면 false.</summary>
    public bool TryScheduleRetry()
    {
        if (Retries >= MaxRetries) return false;
        Retries++;
        return true;
    }
}
