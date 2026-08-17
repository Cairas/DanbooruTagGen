namespace DanbooruTagGen.Core.Generation;

public sealed class GenerationValidationException : Exception
{
    public GenerationValidationException(string message) : base(message) { }
}
