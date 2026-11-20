using System.Collections.Concurrent;
using DanbooruTagGen.Core.Output;

namespace DanbooruTagGen.Core.Tokenization;

/// <summary>출력 줄을 넣을 모델. 텍스트 인코더가 달라 토큰을 세는 법과 길이 기준이 다르다.</summary>
public enum ModelKind
{
    /// <summary>Illustrious(SDXL 계열) — CLIP 토크나이저, 한 청크 77토큰 = 시작·끝 2개 + 실제 75토큰.</summary>
    IL = 0,
    /// <summary>Anima — Qwen3 토크나이저(ComfyUI는 자르지 않음, 공식 구현의 조건 길이 512).</summary>
    Anima = 1,
}

/// <summary>글 한 조각의 토큰 수(시작·끝 같은 특수 토큰은 빼고). <see cref="IsExact"/>가 false면 토크나이저 파일이 없어 어림값이다.</summary>
public interface ITokenCounter
{
    ModelKind Model { get; }
    bool IsExact { get; }
    int Count(string text);
}

public static class TokenCounters
{
    /// <summary>CLIP 한 청크에서 실제로 쓰는 토큰 수(77 − 시작·끝).</summary>
    public const int ClipChunk = 75;

    private static readonly ConcurrentDictionary<(ModelKind, string), ITokenCounter> Cache = new();

    /// <summary>모델의 토크나이저(<paramref name="tokenizersDir"/>/clip 또는 /qwen3의 merges.txt). 없거나 못 읽으면 어림 카운터.
    /// 같은 폴더면 한 번만 읽는다(스레드 안전).</summary>
    public static ITokenCounter For(ModelKind model, string tokenizersDir) =>
        Cache.GetOrAdd((model, Path.GetFullPath(tokenizersDir)), key => Load(key.Item1, key.Item2));

    private static ITokenCounter Load(ModelKind model, string dir)
    {
        var merges = Path.Combine(dir, model == ModelKind.IL ? "clip" : "qwen3", "merges.txt");
        try
        {
            if (File.Exists(merges))
                return model == ModelKind.IL ? BpeTokenCounter.Clip(merges) : BpeTokenCounter.Qwen(merges);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { /* 어림값으로 */ }
        return new EstimateCounter(model);
    }

    /// <summary>ComfyUI처럼 가중치 괄호를 풀어 조각마다 세어 더한 한 줄의 토큰 수.</summary>
    public static int CountLine(ITokenCounter counter, string outputLine)
    {
        int total = 0;
        foreach (var seg in EncoderText.Segments(outputLine)) total += counter.Count(seg);
        return total;
    }

    /// <summary>CLIP 청크 수(75토큰마다 하나).</summary>
    public static int Chunks(int tokens) => tokens <= 0 ? 0 : (tokens + ClipChunk - 1) / ClipChunk;

    /// <summary>기본 길이 기준: IL 225(3청크 — 그 뒤는 반영이 약해진다는 커뮤니티 경험, 공식 수치 아님), Anima 512(공식 구현의 조건 길이).</summary>
    public static int DefaultLimit(ModelKind model) => model == ModelKind.IL ? 3 * ClipChunk : 512;

    /// <summary>토크나이저 파일이 없을 때의 어림값(예전 LineStats.EstimateClipTokens).</summary>
    private sealed class EstimateCounter(ModelKind model) : ITokenCounter
    {
        public ModelKind Model => model;
        public bool IsExact => false;
        public int Count(string text) => LineStats.EstimateClipTokens(text);
    }
}
