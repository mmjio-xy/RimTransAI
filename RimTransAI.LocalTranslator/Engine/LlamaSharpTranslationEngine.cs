using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.Extensions.Logging;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Translation;

namespace RimTransAI.LocalTranslator.Engine;

public sealed class LlamaSharpTranslationEngine(ILogger<LlamaSharpTranslationEngine> logger) : ITranslationEngine
{
    private readonly ILogger<LlamaSharpTranslationEngine> _logger = logger;
    private readonly object _gate = new();
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private int _maxTokens;

    public bool IsLoaded
    {
        get { lock (_gate) return _executor is not null; }
    }

    public async Task LoadAsync(TranslatorOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelPath = options.ResolvedModelPath;
        _logger.LogInformation("开始加载本地翻译模型 Path={ModelPath}", modelPath);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)options.ContextSize,
                GpuLayerCount = options.GpuLayerCount
            };
            var weights = LLamaWeights.LoadFromFile(parameters);
            var executor = new StatelessExecutor(weights, parameters, _logger)
            {
                ApplyTemplate = true
            };

            lock (_gate)
            {
                _weights?.Dispose();
                _weights = weights;
                _executor = executor;
                _maxTokens = options.MaxTokensPerItem;
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("本地翻译模型已加载");
    }

    public async Task<string> TranslateAsync(string sourceText, CancellationToken cancellationToken)
    {
        StatelessExecutor executor;
        int maxTokens;
        lock (_gate)
        {
            executor = _executor ?? throw new InvalidOperationException("翻译模型尚未加载。");
            maxTokens = _maxTokens;
        }

        var output = new StringBuilder();
        var inference = new InferenceParams { MaxTokens = maxTokens };
        await foreach (var piece in executor.InferAsync(PromptTemplate.Create(sourceText), inference, cancellationToken)
                           .ConfigureAwait(false))
        {
            output.Append(piece);
        }

        return output.ToString().Trim();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _weights?.Dispose();
            _executor = null;
            _weights = null;
        }
    }
}
