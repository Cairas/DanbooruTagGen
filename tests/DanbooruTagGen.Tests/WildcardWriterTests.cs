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
}
