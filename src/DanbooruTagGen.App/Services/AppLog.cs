using DanbooruTagGen.Core.Logging;

namespace DanbooruTagGen.App.Services;

/// <summary>앱의 문제 로그(프로그램 문제·컨셉 제작 문제) — <see cref="ProblemLog"/>를 %APPDATA%\DanbooruTagGen\logs에 둔다.
/// 쓰기는 백그라운드에서 하고, 실패하면 <see cref="WriteFailed"/>로 알린다(로그 때문에 생성·화면이 멈추면 안 된다).
/// 백업 복원 후 재시작 중(<see cref="AppWriteGuard"/>)에도 로그는 쓴다 — 사용자 데이터가 아니다.</summary>
public static class AppLog
{
    public static ProblemLog Log { get; } = new(AppPaths.LogsDir);

    /// <summary>로그 쓰기가 실패했을 때(파일 잠금 등). 메인 뷰모델이 상태 표시줄에 띄운다.</summary>
    public static event Action<string>? WriteFailed;

    /// <summary>같은 문제는 하루 한 번만 남긴다(앱이 켜져 있는 동안) — 되풀이되는 같은 오류로 로그가 뒤덮이지 않게.</summary>
    private static readonly RepeatFilter Repeats = new();

    /// <summary>프로그램 문제. 제목·내용이 오늘 이미 남긴 것과 같으면 건너뛴다.</summary>
    public static void Program(string title, string body)
    {
        var now = DateTime.Now;
        if (!Repeats.IsNew("P|" + title + "\n" + body, now)) return;
        Enqueue(new[] { new LogEntry(now, LogKind.Program, title, null, body) });
    }

    /// <summary>컨셉 제작 문제(일괄 생성 결과). 같은 열쇠(<see cref="GenerationProblems.RepeatKey"/> — 같은 레시피 내용의 같은 문제)는
    /// 오늘 이미 남겼으면 건너뛴다. 반환: 새로 남긴 항목 수.</summary>
    public static int Concept(IReadOnlyList<(LogEntry Entry, string RepeatKey)> items)
    {
        var fresh = items.Where(i => Repeats.IsNew("C|" + i.RepeatKey, i.Entry.At)).Select(i => i.Entry).ToList();
        if (fresh.Count > 0) Enqueue(fresh);
        return fresh.Count;
    }

    /// <summary>처리하지 못한 오류: 종류·메시지·스택 앞 5줄·빌드 시각.</summary>
    public static void Exception(string title, Exception ex)
    {
        var stack = (ex.StackTrace ?? "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(5);
        Program(title, $"{ex.GetType().Name}: {ex.Message}\n" + string.Join("\n", stack) + $"\n빌드 {AppPaths.BuildTime:MM-dd HH:mm}");
    }

    private static void Enqueue(IReadOnlyList<LogEntry> entries) =>
        _ = Task.Run(() =>
        {
            if (!Log.WriteAll(entries))
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    WriteFailed?.Invoke("⚠ 로그를 쓰지 못했습니다(다른 프로그램이 로그 파일을 열고 있나요?) — " + Log.PathOf(DateTime.Now)));
        });
}
