namespace DysonNetwork.Sphere.Translation;

public interface ITranslationProvider
{
    public Task<string> Translate(string text, string targetLanguage, string? sourceLanguage = null);
}