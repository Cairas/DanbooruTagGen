using System.Text.Json;
using DanbooruTagGen.Core.Tokenization;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>실제 토크나이저(IL = CLIP, Anima = Qwen3). 기준값은 tools/tokenizer_reference.py가 HF transformers로 만든 것 —
/// ComfyUI처럼 가중치 괄호를 풀어 조각마다 세고(시작·끝 토큰 제외) 더한다.</summary>
public class TokenizerTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "tokenizers"))) return dir.FullName;
        throw new DirectoryNotFoundException("data/tokenizers를 찾지 못했습니다.");
    }

    private static string TokenizersDir => Path.Combine(RepoRoot(), "data", "tokenizers");

    private sealed record Reference(string Text, int Il, int Anima);

    [Fact]
    public void EncoderTextStripsWeightsAndUnescapes()
    {
        Assert.Equal("a, b", EncoderText.Of("(a:1.2), b"));
        Assert.Equal("piledriver_(sex)", EncoderText.Of("piledriver_\\(sex\\)"));
        Assert.Equal("x, y", EncoderText.Of("((x)), y"));
        Assert.Equal("smile", EncoderText.Of("smile"));
        Assert.Equal(new[] { "a", "b", "c" }, EncoderText.Segments("a(b:1.2)c"));   // ComfyUI는 조각마다 따로 토큰화한다
    }

    [Fact]
    public void CountsMatchHuggingFaceReference()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "DanbooruTagGen.Tests", "TestData", "tokenizer_reference.json"));
        var refs = JsonSerializer.Deserialize<List<Reference>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(refs.Count >= 150);
        var il = TokenCounters.For(ModelKind.IL, TokenizersDir);
        var anima = TokenCounters.For(ModelKind.Anima, TokenizersDir);
        Assert.True(il.IsExact);
        Assert.True(anima.IsExact);
        var wrong = refs.Where(r => TokenCounters.CountLine(il, r.Text) != r.Il || TokenCounters.CountLine(anima, r.Text) != r.Anima)
            .Select(r => $"{r.Text[..Math.Min(60, r.Text.Length)]} → IL {TokenCounters.CountLine(il, r.Text)}/{r.Il}, Anima {TokenCounters.CountLine(anima, r.Text)}/{r.Anima}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void MissingFilesFallBackToEstimate()
    {
        var c = TokenCounters.For(ModelKind.Anima, Path.Combine(Path.GetTempPath(), "no_such_tokenizers_" + Guid.NewGuid().ToString("N")));
        Assert.False(c.IsExact);
        Assert.True(TokenCounters.CountLine(c, "1girl, solo") > 0);
    }

    /// <summary>글자 수 = 토큰 수인 가짜 카운터(판정 로직만 보려고).</summary>
    internal sealed class LengthCounter(ModelKind model, bool exact = true) : ITokenCounter
    {
        public ModelKind Model => model;
        public bool IsExact => exact;
        public int Count(string text) => text.Length;
    }

    [Fact]
    public void LineStatsUsesCounterAndLimit()
    {
        var s = Core.Output.LineStats.Measure(new[] { "aaaa", "", "bb" }, new LengthCounter(ModelKind.Anima), tokenLimit: 3);
        Assert.Equal(2, s.LineCount);
        Assert.Equal(4, s.TokensMax);
        Assert.Equal(3, s.TokensAverage);
        Assert.Equal(1, s.OverLimit);
        Assert.Equal(3, s.TokenLimit);
        Assert.Equal(0, s.LongestLineIndex);
        Assert.Equal(ModelKind.Anima, s.Model);
        Assert.True(s.IsExact);
    }

    [Fact]
    public void RecipeCheckShowsBothModels()
    {
        var recipe = new Core.Models.Recipe { Name = "길이", Slots = { new Core.Models.FixedSlot { Tags = { "1girl", "solo" } } } };
        var (_, text) = Core.Generation.RecipeCheckReport.Build(recipe, new Dictionary<string, Core.Models.Pool>(),
            new Core.Models.GenerationOptions { LineCount = 10, Seed = 1 }, Core.Generation.ConflictRules.Empty, tagInfo: null,
            deep: false, il: new LengthCounter(ModelKind.IL), anima: new LengthCounter(ModelKind.Anima, exact: false));
        var line = text.Split('\n').Single(l => l.Contains("토큰 —"));
        Assert.Contains("IL 평균 11", line);           // "1girl, solo" = 11글자
        Assert.Contains("Anima 평균 11", line);
        Assert.Contains("(추정)", line);                // Anima 쪽이 어림값
    }

    /// <summary>git(autocrlf)이 merges.txt를 CRLF로 바꿔 받아도 같은 수를 세야 한다.</summary>
    [Fact]
    public void CrlfMergesCountTheSame()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "dtg-crlf-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var sub in new[] { "clip", "qwen3" })
            {
                Directory.CreateDirectory(Path.Combine(tmp, sub));
                var lf = File.ReadAllText(Path.Combine(TokenizersDir, sub, "merges.txt")).Replace("\r\n", "\n");
                File.WriteAllText(Path.Combine(tmp, sub, "merges.txt"), lf.Replace("\n", "\r\n"));
            }
            const string text = "1girl, solo, long_hair, looking_at_viewer, piledriver_(sex), (blush:1.2), heart-shaped_pupils";
            foreach (var model in new[] { ModelKind.IL, ModelKind.Anima })
                Assert.Equal(TokenCounters.CountLine(TokenCounters.For(model, TokenizersDir), text),
                             TokenCounters.CountLine(TokenCounters.For(model, tmp), text));
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    [Fact]
    public void ChunksOfSeventyFive()
    {
        Assert.Equal(0, TokenCounters.Chunks(0));
        Assert.Equal(1, TokenCounters.Chunks(75));
        Assert.Equal(2, TokenCounters.Chunks(76));
        Assert.Equal(3, TokenCounters.Chunks(225));
        Assert.Equal(225, TokenCounters.DefaultLimit(ModelKind.IL));
        Assert.Equal(512, TokenCounters.DefaultLimit(ModelKind.Anima));
    }
}
