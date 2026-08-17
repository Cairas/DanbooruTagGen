using System.Text;

namespace DanbooruTagGen.Core.Output;

public static class WildcardWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string path, IReadOnlyList<string> lines, WriteMode mode, bool insertBlankLineSeparator)
    {
        string block = string.Join("\n", lines);

        switch (mode)
        {
            case WriteMode.New:
                if (File.Exists(path))
                    throw new IOException($"파일이 이미 존재합니다: {path}");
                File.WriteAllText(path, block, Utf8NoBom);
                break;

            case WriteMode.Overwrite:
                File.WriteAllText(path, block, Utf8NoBom);
                break;

            case WriteMode.Append:
                string existing = File.Exists(path) ? File.ReadAllText(path) : "";
                var sb = new StringBuilder(existing);
                if (existing.Length > 0)
                {
                    if (!existing.EndsWith('\n')) sb.Append('\n');
                    if (insertBlankLineSeparator) sb.Append('\n');
                }
                sb.Append(block);
                File.WriteAllText(path, sb.ToString(), Utf8NoBom);
                break;
        }
    }
}
