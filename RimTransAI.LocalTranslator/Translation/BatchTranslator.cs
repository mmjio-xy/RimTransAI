using Microsoft.Extensions.Logging;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Engine;

namespace RimTransAI.LocalTranslator.Translation;

public sealed class BatchTranslator : IDisposable
{
    private readonly ITranslationEngine _engine;
    private readonly TranslatorOptions _options;
    private readonly ILogger<BatchTranslator> _logger;
    private readonly SemaphoreSlim _queueSlots;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);

    public BatchTranslator(
        ITranslationEngine engine,
        TranslatorOptions options,
        ILogger<BatchTranslator> logger)
    {
        _engine = engine;
        _options = options;
        _logger = logger;
        _queueSlots = new SemaphoreSlim(options.MaxQueueLength, options.MaxQueueLength);
    }

    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<KeyValuePair<string, string>> entries,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!_queueSlots.Wait(0)) throw new TranslationQueueFullException();

        var queueStart = DateTimeOffset.UtcNow;
        try
        {
            await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _logger.LogInformation(
                    "开始翻译请求 RequestId={RequestId} Items={Items} QueueWaitMs={QueueWaitMs}",
                    requestId,
                    entries.Count,
                    (DateTimeOffset.UtcNow - queueStart).TotalMilliseconds);

                var result = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
                var fallbackCount = 0;
                foreach (var (key, source) in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var translated = await _engine.TranslateAsync(source, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(translated))
                        {
                            fallbackCount++;
                            result[key] = source;
                            _logger.LogWarning("模型返回空翻译，保留原文 RequestId={RequestId}", requestId);
                        }
                        else
                        {
                            result[key] = translated;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        fallbackCount++;
                        result[key] = source;
                        _logger.LogError(exception, "单条翻译失败，保留原文 RequestId={RequestId} Key={Key}", requestId, key);
                    }
                }

                _logger.LogInformation(
                    "翻译请求完成 RequestId={RequestId} Items={Items} Fallbacks={Fallbacks}",
                    requestId,
                    entries.Count,
                    fallbackCount);
                return result;
            }
            finally
            {
                _inferenceGate.Release();
            }
        }
        finally
        {
            _queueSlots.Release();
        }
    }

    public void Dispose()
    {
        _queueSlots.Dispose();
        _inferenceGate.Dispose();
    }
}

public sealed class TranslationQueueFullException : Exception;
