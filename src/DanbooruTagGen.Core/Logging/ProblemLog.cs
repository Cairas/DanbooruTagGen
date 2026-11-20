using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DanbooruTagGen.Core.Logging;

/// <summary>로그 항목 종류: 컨셉(레시피 제작) 문제 / 프로그램 문제.</summary>
public enum LogKind { Concept, Program }

/// <summary>로그 한 항목. <see cref="Recipe"/>는 컨셉 문제일 때의 레시피 이름(없으면 null). <see cref="Body"/>는 여러 줄일 수 있다.</summary>
public sealed record LogEntry(DateTime At, LogKind Kind, string Title, string? Recipe, string Body);

/// <summary>문제만 남기는 로그(프로그램 문제·컨셉 제작 문제 파악용). 하루 한 파일 <c>{dir}/yyyy-MM-dd.log</c>, 일반 텍스트라
/// 다른 작업 세션에 그대로 넘길 수 있다. 항목 형식:
/// <code>
/// [15:30:12] [컨셉] 생성 · 조합 바닥 · 시드 7
///   레시피: 🔞 이름
///     본문 첫 줄
///     본문 둘째 줄
/// (빈 줄)
/// </code>
/// 쓰기가 실패해도(파일 잠금 등) 예외를 던지지 않는다 — 로그 때문에 생성이 멈추면 안 된다.</summary>
public sealed class ProblemLog
{
    private const string DayFormat = "yyyy-MM-dd";
    private static readonly Regex Header = new(@"^\[(\d\d:\d\d:\d\d)\] \[(컨셉|프로그램)\] (.*)$", RegexOptions.Compiled);
    private const string RecipePrefix = "  레시피: ";
    private const string BodyIndent = "    ";
    private static readonly object Gate = new();

    private readonly string _dir;

    public ProblemLog(string dir) => _dir = dir;

    public string Directory => _dir;

    public string PathOf(DateTime day) => Path.Combine(_dir, day.ToString(DayFormat, CultureInfo.InvariantCulture) + ".log");

    public bool Write(LogEntry e)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(e.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("] [")
          .Append(e.Kind == LogKind.Concept ? "컨셉" : "프로그램").Append("] ").Append(OneLine(e.Title)).Append('\n');
        if (!string.IsNullOrEmpty(e.Recipe)) sb.Append(RecipePrefix).Append(OneLine(e.Recipe)).Append('\n');
        foreach (var line in e.Body.Replace("\r\n", "\n").Split('\n'))
            sb.Append(BodyIndent).Append(line).Append('\n');
        sb.Append('\n');
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(_dir);
                File.AppendAllText(PathOf(e.At), sb.ToString(), new UTF8Encoding(false));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public bool WriteAll(IEnumerable<LogEntry> entries)
    {
        bool ok = true;
        foreach (var e in entries) ok &= Write(e);
        return ok;
    }

    /// <summary>그날의 항목들(쓴 순서).</summary>
    public IReadOnlyList<LogEntry> Read(DateTime day)
    {
        var path = PathOf(day);
        if (!File.Exists(path)) return Array.Empty<LogEntry>();
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<LogEntry>(); }

        var result = new List<LogEntry>();
        DateTime at = default;
        LogKind kind = LogKind.Program;
        string? title = null, recipe = null;
        var body = new List<string>();
        void Flush()
        {
            if (title != null) result.Add(new LogEntry(at, kind, title, recipe, string.Join("\n", body)));
            title = null; recipe = null; body.Clear();
        }
        foreach (var line in lines)
        {
            var m = Header.Match(line);
            if (m.Success)
            {
                Flush();
                at = day.Date + TimeSpan.ParseExact(m.Groups[1].Value, @"hh\:mm\:ss", CultureInfo.InvariantCulture);
                kind = m.Groups[2].Value == "컨셉" ? LogKind.Concept : LogKind.Program;
                title = m.Groups[3].Value;
            }
            else if (title != null && body.Count == 0 && recipe == null && line.StartsWith(RecipePrefix, StringComparison.Ordinal))
                recipe = line[RecipePrefix.Length..];
            else if (title != null && line.StartsWith(BodyIndent, StringComparison.Ordinal))
                body.Add(line[BodyIndent.Length..]);
            else if (line.Length == 0) Flush();
        }
        Flush();
        return result;
    }

    /// <summary>로그가 있는 날짜들, 최신순.</summary>
    public IReadOnlyList<DateTime> Days()
    {
        if (!System.IO.Directory.Exists(_dir)) return Array.Empty<DateTime>();
        return System.IO.Directory.EnumerateFiles(_dir, "*.log")
            .Select(p => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue).Select(d => d!.Value)
            .OrderByDescending(d => d).ToList();
    }

    /// <summary>오늘 포함 <paramref name="keepDays"/>일치만 남기고 지운다. 반환: 지운 파일 수.</summary>
    public int Prune(DateTime now, int keepDays = 7)
    {
        var oldest = now.Date.AddDays(-(keepDays - 1));
        int n = 0;
        foreach (var day in Days().Where(d => d < oldest))
        {
            try { File.Delete(PathOf(day)); n++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 다음 실행 때 다시 */ }
        }
        return n;
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ");
}
