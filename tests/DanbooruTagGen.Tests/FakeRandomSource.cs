using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.Tests;

/// <summary>정해둔 값을 순서대로 반환(각각 maxExclusive로 모듈러). 소진되면 0.</summary>
public sealed class FakeRandomSource : IRandomSource
{
    private readonly int[] _values;
    private int _i;
    public FakeRandomSource(params int[] values) => _values = values;
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) return 0;
        int v = _i < _values.Length ? _values[_i++] : 0;
        return ((v % maxExclusive) + maxExclusive) % maxExclusive;
    }
}
