namespace DanbooruTagGen.Core.Generation;

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;
    public SystemRandomSource(int? seed = null)
        => _random = seed.HasValue ? new Random(seed.Value) : new Random();
    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
