using System.Text;

namespace DanbooruTagGen.Core.Output;

public static class WildcardWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>레시피 이름들을 파일명으로 바꾼다(입력 순서 그대로).
    ///
    /// 레시피별로 파일을 나눠 뽑으면 그 파일명이 곧 와일드카드 토큰이 되므로, 이름을 최대한
    /// 그대로 살린다(🔞·공백·괄호 유지). 손대는 건 파일 시스템이나 와일드카드 선택기가 거부하는 것뿐이다:
    /// 금지문자 제거, 쉼표→" · ", 맨 앞 '#' 제거, 윈도우가 저장하지 못하는 <b>끝의 공백·마침표</b> 정리,
    /// 남은 게 없으면 fallback(레시피 id) 사용.
    ///
    /// <para>이름이 겹치면 뒤엣것에 "-2"부터 번호를 붙인다 — 안 그러면 같은 이름의 팩이
    /// 서로를 조용히 덮어써서 파일 수가 모자란 이유를 알 수 없게 된다. 윈도우 파일
    /// 시스템은 대소문자를 구분하지 않으므로 겹침 판정도 대소문자를 무시한다.</para></summary>
    public static IReadOnlyList<string> ToFileNames(IEnumerable<(string Name, string Fallback)> recipes)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var (name, fallback) in recipes)
        {
            var baseName = Sanitize(name);
            if (baseName.Length == 0) baseName = Sanitize(fallback);
            if (baseName.Length == 0) baseName = "recipe";

            var candidate = baseName;
            for (int n = 2; !used.Add(candidate); n++)
                candidate = $"{baseName}-{n}";

            result.Add(candidate);
        }

        return result;
    }

    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var kept = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (Array.IndexOf(invalid, c) < 0)
                kept.Append(c);
        // 파일명이 곧 와일드카드 토큰이라, 선택 목록을 쉼표로 나누는 소비자(VWildCard)에선
        // 쉼표 든 이름이 두 조각으로 잘려 "파일 없음"이 된다. 같은 이유로 맨 앞 '#'은 비활성 표시로 읽힌다.
        var name = CommaRun.Replace(kept.ToString(), " · ").TrimStart('#', ' ');
        // 윈도우는 이름 끝의 공백·마침표를 저장하지 못한다(조용히 잘리거나 열리지 않는다).
        return name.TrimEnd(' ', '.').Trim();
    }

    private static readonly System.Text.RegularExpressions.Regex CommaRun = new(@"\s*,\s*");

    /// <summary>줄들을 파일에 쓴다.
    /// <para>기존 파일을 잃지 않는 쪽으로 쓴다. 예전엔 Append가 파일 전체를 읽어 다시 썼고 Overwrite도 제자리에
    /// 썼다 — 쓰는 도중 죽거나 디스크가 차면 수천 줄 쌓아 둔 와일드카드 파일이 통째로 깨졌다.
    /// <list type="bullet">
    /// <item>New/Overwrite: 임시 파일에 다 쓴 뒤 교체한다(원자적). 실패하면 기존 파일은 그대로다.</item>
    /// <item>Append: 끝에 덧붙이기만 한다(기존 바이트는 건드리지 않는다). 기존 파일 크기와 무관하게 O(추가분)이다.
    /// UTF-16/32 BOM 파일만은 바이트를 덧붙이면 깨지므로 예전처럼 읽어서 UTF-8로 다시 쓴다(이때도 원자적 교체).</item>
    /// </list></para></summary>
    public static void Write(string path, IReadOnlyList<string> lines, WriteMode mode, bool insertBlankLineSeparator)
    {
        string block = string.Join("\n", lines);

        switch (mode)
        {
            case WriteMode.New:
                if (File.Exists(path))
                    throw new IOException($"파일이 이미 존재합니다: {path}");
                WriteAtomic(path, block, allowReplace: false);
                break;

            case WriteMode.Overwrite:
                WriteAtomic(path, block, allowReplace: true);
                break;

            case WriteMode.Append:
                Append(path, block, insertBlankLineSeparator);
                break;
        }
    }

    private static void Append(string path, string block, bool insertBlankLineSeparator)
    {
        if (File.Exists(path) && HasWideUnicodeBom(path))
        {
            string existing = File.ReadAllText(path);
            WriteAtomic(path, existing + Separator(existing.Length > 0, existing.EndsWith('\n'), insertBlankLineSeparator) + block,
                allowReplace: true);
            return;
        }

        using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        // UTF-8 BOM만 있는 파일은 내용이 빈 것이다(예전 ReadAllText 기준과 같게 — 앞에 빈 줄을 만들지 않는다).
        bool hasContent = fs.Length > Utf8Bom.Length || (fs.Length > 0 && !StartsWith(fs, Utf8Bom));
        bool endsWithNewline = false;
        if (hasContent)
        {
            fs.Seek(-1, SeekOrigin.End);
            endsWithNewline = fs.ReadByte() == '\n';
        }
        fs.Seek(0, SeekOrigin.End);
        var bytes = Utf8NoBom.GetBytes(Separator(hasContent, endsWithNewline, insertBlankLineSeparator) + block);
        fs.Write(bytes, 0, bytes.Length);
    }

    /// <summary>기존 내용과 새 블록 사이에 넣을 문자열: 마지막 줄을 끝맺고, 원하면 빈 줄 하나.</summary>
    private static string Separator(bool hasContent, bool endsWithNewline, bool blankLine) =>
        !hasContent ? "" : (endsWithNewline ? "" : "\n") + (blankLine ? "\n" : "");

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private static bool StartsWith(FileStream fs, byte[] prefix)
    {
        if (fs.Length < prefix.Length) return false;
        fs.Seek(0, SeekOrigin.Begin);
        foreach (var b in prefix)
            if (fs.ReadByte() != b) return false;
        return true;
    }

    /// <summary>UTF-16(LE/BE)·UTF-32 BOM으로 시작하는가 — 이런 파일에 UTF-8 바이트를 덧붙이면 깨진다.</summary>
    private static bool HasWideUnicodeBom(string path)
    {
        Span<byte> head = stackalloc byte[2];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int n = fs.Read(head);
        return n == 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF) || (head[0] == 0x00 && head[1] == 0x00));
    }

    /// <summary>같은 폴더의 임시 파일에 다 쓴 뒤 교체한다. 교체가 지원되지 않는 위치(일부 네트워크 드라이브 등)면
    /// 복사로 폴백한다 — 그 경우도 임시 파일은 이미 완성돼 있으므로 쓰다 만 내용이 대상에 남지 않는다.</summary>
    private static void WriteAtomic(string path, string text, bool allowReplace)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, Utf8NoBom);
        try
        {
            if (allowReplace && File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    File.Copy(tmp, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tmp, path, overwrite: false);
            }
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
