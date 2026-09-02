using RimTransAI.LocalTranslator.Configuration;

namespace RimTransAI.LocalTranslator.Engine;

public interface ITranslationEngine : IDisposable
{
    bool IsLoaded { get; }

    Task LoadAsync(TranslatorOptions options, CancellationToken cancellationToken);

    Task<string> TranslateAsync(string sourceText, CancellationToken cancellationToken);
}
