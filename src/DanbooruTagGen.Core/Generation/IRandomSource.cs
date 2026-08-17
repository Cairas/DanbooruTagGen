namespace DanbooruTagGen.Core.Generation;

/// <summary>난수 추상화. 테스트에서 결정적 시퀀스를 주입하기 위함.</summary>
public interface IRandomSource
{
    /// <summary>[0, maxExclusive) 범위의 정수.</summary>
    int Next(int maxExclusive);
}
