using System.Text;
using DanbooruTagGen.Core.Output;
using Xunit;

namespace DanbooruTagGen.Tests;

public class WildcardWriterTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), $"wc_{Guid.NewGuid():N}.txt");

    [Fact]
    public void NewWritesLines()
    {
        var path = Temp();
        try
        {
            WildcardWriter.Write(path, new[] { "a", "b" }, WriteMode.New, false);
            Assert.Equal("a\nb", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NewThrowsIfExists()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "x");
            Assert.Throws<IOException>(() => WildcardWriter.Write(path, new[] { "a" }, WriteMode.New, false));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OverwriteReplaces()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "old\ncontent");
            WildcardWriter.Write(path, new[] { "a", "b" }, WriteMode.Overwrite, false);
            Assert.Equal("a\nb", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppendEnsuresNewlineSeparation()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "a\nb"); // 마지막 줄 개행 없음
            WildcardWriter.Write(path, new[] { "c", "d" }, WriteMode.Append, false);
            Assert.Equal("a\nb\nc\nd", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppendInsertsBlankLineSeparator()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "a\nb");
            WildcardWriter.Write(path, new[] { "c" }, WriteMode.Append, true);
            Assert.Equal("a\nb\n\nc", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppendToEmptyFileHasNoLeadingBlank()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "");
            WildcardWriter.Write(path, new[] { "c", "d" }, WriteMode.Append, true);
            Assert.Equal("c\nd", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppendWhenExistingEndsWithNewline()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "a\nb\n");
            WildcardWriter.Write(path, new[] { "c" }, WriteMode.Append, false);
            Assert.Equal("a\nb\nc", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    // ── 레시피별 파일 분리 출력용 파일명 만들기 ──────────────────────────

    [Fact]
    public void KeepsRecipeNameAsIsWhenItIsAlreadyAValidFileName()
    {
        var names = WildcardWriter.ToFileNames(new[] { ("🔞 요바이 (들키면 안 되는 밤)", "id1") });

        Assert.Equal("🔞 요바이 (들키면 안 되는 밤)", names[0]);
    }

    [Fact]
    public void RemovesCharactersWindowsForbidsInFileNames()
    {
        var names = WildcardWriter.ToFileNames(new[] { ("a/b:c*d?e\"f<g>h|i", "id1") });

        Assert.Equal("abcdefghi", names[0]);
    }

    [Fact]
    public void TrimsTrailingDotsAndSpacesThatWindowsCannotStore()
    {
        var names = WildcardWriter.ToFileNames(new[] { ("이름... ", "id1") });

        Assert.Equal("이름", names[0]);
    }

    [Fact]
    public void FallsBackToTheRecipeIdWhenNothingIsLeft()
    {
        var names = WildcardWriter.ToFileNames(new[] { ("///", "preset-r-n415") });

        Assert.Equal("preset-r-n415", names[0]);
    }

    [Fact]
    public void MakesDuplicateNamesUniqueSoTheyDoNotOverwriteEachOther()
    {
        var names = WildcardWriter.ToFileNames(new[] { ("촉수", "a"), ("촉수", "b"), ("촉수", "c") });

        Assert.Equal(new[] { "촉수", "촉수-2", "촉수-3" }, names);
    }

    [Fact]
    public void TreatsNamesDifferingOnlyInCaseAsDuplicates()
    {
        // 윈도우 파일 시스템은 대소문자를 구분하지 않는다 — 구분해서 세면 한쪽이 조용히 덮인다.
        var names = WildcardWriter.ToFileNames(new[] { ("Tentacle", "a"), ("tentacle", "b") });

        Assert.Equal(new[] { "Tentacle", "tentacle-2" }, names);
    }
}
