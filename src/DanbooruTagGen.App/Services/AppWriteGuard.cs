namespace DanbooruTagGen.App.Services;

/// <summary>백업 복원 직후 앱을 다시 시작하는 동안 사용자 데이터 쓰기를 막는다. 안 막으면 창을 닫을 때의 자동 저장(빌더·생성 탭 설정·
/// 라이브러리)이 방금 되돌린 파일을 메모리의 옛 상태로 다시 덮어쓴다. 한 번 얼리면 프로세스가 끝날 때까지 풀지 않는다.</summary>
public static class AppWriteGuard
{
    public static bool Frozen { get; private set; }
    public static void Freeze() => Frozen = true;

    /// <summary>백업 복원이 다시 띄운 실행인가(<see cref="AfterRestoreArg"/>). 이 실행에선 켤 때 프리셋 자동 갱신을 건너뛴다 —
    /// 안 그러면 방금 되돌린 제공 팩이 켜자마자 지금 번들 내용으로 다시 덮어써진다.</summary>
    public static bool StartedAfterRestore { get; private set; }
    public const string AfterRestoreArg = "--after-restore";
    public static void MarkStartedAfterRestore() => StartedAfterRestore = true;
}
