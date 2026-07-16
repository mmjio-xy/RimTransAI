using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 多线程翻译服务
/// </summary>
public class MultiThreadedTranslationService : IDisposable
{
    private readonly LlmService _llmService;
    private readonly bool _ownsLlmService;
    private readonly Func<Action, Task> _applyItemUpdatesAsync;
    private readonly ILogger<MultiThreadedTranslationService> _logger;
    private bool _disposed;

    public MultiThreadedTranslationService()
        : this(
            new LlmService(),
            ownsLlmService: true,
            ApplyUpdatesInlineAsync,
            NullLogger<MultiThreadedTranslationService>.Instance)
    {
    }

    /// <summary>
    /// 使用外部提供的 LLM 服务。外部服务的生命周期仍由调用方管理。
    /// </summary>
    public MultiThreadedTranslationService(LlmService llmService)
        : this(
            llmService,
            ownsLlmService: false,
            ApplyUpdatesInlineAsync,
            NullLogger<MultiThreadedTranslationService>.Instance)
    {
    }

    /// <summary>
    /// 使用外部 LLM 服务，并通过指定调度器应用 TranslationItem 属性更新。
    /// </summary>
    public MultiThreadedTranslationService(
        LlmService llmService,
        Func<Action, Task> applyItemUpdatesAsync,
        ILogger<MultiThreadedTranslationService>? logger = null)
        : this(
            llmService,
            ownsLlmService: false,
            applyItemUpdatesAsync,
            logger ?? NullLogger<MultiThreadedTranslationService>.Instance)
    {
    }

    private MultiThreadedTranslationService(
        LlmService llmService,
        bool ownsLlmService,
        Func<Action, Task> applyItemUpdatesAsync,
        ILogger<MultiThreadedTranslationService> logger)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _ownsLlmService = ownsLlmService;
        _applyItemUpdatesAsync = applyItemUpdatesAsync
            ?? throw new ArgumentNullException(nameof(applyItemUpdatesAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static Task ApplyUpdatesInlineAsync(Action update)
    {
        update();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行多线程翻译
    /// </summary>
    /// <param name="batchResult">分批结果</param>
    /// <param name="concurrencyManager">并发控制器</param>
    /// <param name="progressReporter">进度报告器</param>
    /// <param name="apiKey">API Key</param>
    /// <param name="apiUrl">API URL</param>
    /// <param name="model">模型名称</param>
    /// <param name="targetLang">目标语言</param>
    /// <param name="customPrompt">自定义提示词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功完成的批次数</returns>
    public async Task<int> ExecuteBatchesAsync(
        BatchingService.BatchResult batchResult,
        ConcurrencyManager concurrencyManager,
        ThreadSafeProgressReporter progressReporter,
        string apiKey,
        string apiUrl,
        string model,
        string targetLang,
        int requestTimeoutSeconds,
        string? customPrompt = null,
        bool autoCompleteApiUrl = true,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MultiThreadedTranslationService));

        if (batchResult.Batches.Count == 0)
            return 0;

        var processedBatches = 0;
        var successfulBatches = 0;

        var nextBatchIndex = -1;
        var workerCount = Math.Min(
            concurrencyManager.MaxConcurrentRequests,
            batchResult.TotalBatches);

        // 只创建固定数量的 worker，避免为每个批次同时创建 Task 和取消令牌注册。
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => ProcessBatchesAsync())
            .ToList();

        await Task.WhenAll(workers);

        return successfulBatches;

        async Task ProcessBatchesAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchIndex = Interlocked.Increment(ref nextBatchIndex);
                if (batchIndex >= batchResult.TotalBatches)
                    return;

                await ProcessBatchWithProgressAsync(
                    batchResult.Batches[batchIndex],
                    batchIndex + 1,
                    batchResult.TotalBatches,
                    concurrencyManager,
                    progressReporter,
                    apiKey,
                    apiUrl,
                    model,
                    targetLang,
                    requestTimeoutSeconds,
                    customPrompt,
                    autoCompleteApiUrl,
                    () => Interlocked.Increment(ref processedBatches),
                    () => Interlocked.Increment(ref successfulBatches),
                    cancellationToken);
            }
        }
    }

    private async Task ProcessBatchWithProgressAsync(
        List<IGrouping<string, TranslationItem>> batch,
        int batchIndex,
        int totalBatches,
        ConcurrencyManager concurrencyManager,
        ThreadSafeProgressReporter progressReporter,
        string apiKey,
        string apiUrl,
        string model,
        string targetLang,
        int requestTimeoutSeconds,
        string? customPrompt,
        bool autoCompleteApiUrl,
        Func<int> markBatchProcessed,
        Func<int> markBatchSuccessful,
        CancellationToken cancellationToken)
    {
        var succeeded = await ProcessBatchAsync(
            batch,
            batchIndex,
            totalBatches,
            concurrencyManager,
            progressReporter,
            apiKey,
            apiUrl,
            model,
            targetLang,
            requestTimeoutSeconds,
            customPrompt,
            autoCompleteApiUrl,
            cancellationToken);

        if (succeeded)
        {
            markBatchSuccessful();
        }

        var processed = markBatchProcessed();
        progressReporter.ReportProgress(
            processed,
            totalBatches,
            concurrencyManager.RunningCount,
            $"批次 {batchIndex}/{totalBatches}");
    }

    /// <summary>
    /// 处理单个批次
    /// </summary>
    private async Task<bool> ProcessBatchAsync(
        List<IGrouping<string, TranslationItem>> batch,
        int batchIndex,
        int totalBatches,
        ConcurrencyManager concurrencyManager,
        ThreadSafeProgressReporter progressReporter,
        string apiKey,
        string apiUrl,
        string model,
        string targetLang,
        int requestTimeoutSeconds,
        string? customPrompt,
        bool autoCompleteApiUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            // 使用并发控制器执行翻译
            _logger.LogDebug(
                "翻译批次等待并发槽 BatchIndex={BatchIndex} TotalBatches={TotalBatches} GroupCount={GroupCount}",
                batchIndex,
                totalBatches,
                batch.Count);
            var translations = await concurrencyManager.ExecuteAsync(async ct =>
            {
                // 进入受控并发区后再记“开始”，避免日志看起来所有批次同时启动。
                _logger.LogDebug(
                    "翻译批次开始请求 BatchIndex={BatchIndex} TotalBatches={TotalBatches}",
                    batchIndex,
                    totalBatches);
                progressReporter.ReportLog($"开始处理批次 {batchIndex}/{totalBatches}，包含 {batch.Count} 个翻译组");
                return await _llmService.TranslateBatchAsync(
                    apiKey,
                    BuildSourceDictionary(batch),
                    apiUrl,
                    model,
                    targetLang,
                    customPrompt,
                    requestTimeoutSeconds,
                    autoCompleteApiUrl,
                    ct);
            }, cancellationToken);

            // 应用翻译结果
            var missingCount = 0;
            await _applyItemUpdatesAsync(() => missingCount = ApplyTranslations(batch, translations));
            _logger.LogDebug(
                "翻译批次结果已应用 BatchIndex={BatchIndex} TotalBatches={TotalBatches} TranslationCount={TranslationCount} MissingCount={MissingCount}",
                batchIndex,
                totalBatches,
                translations.Count,
                missingCount);

            if (missingCount > 0)
            {
                _logger.LogWarning(
                    "翻译批次结果不完整 BatchIndex={BatchIndex} TotalBatches={TotalBatches} MissingCount={MissingCount}",
                    batchIndex,
                    totalBatches,
                    missingCount);
                progressReporter.ReportLog($"⚠ 批次 {batchIndex}/{totalBatches} 部分完成，缺少 {missingCount} 个翻译结果");
                return false;
            }

            // 报告批次完成
            progressReporter.ReportLog($"✓ 批次 {batchIndex}/{totalBatches} 完成，翻译 {batch.Count} 个文本");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "翻译批次已取消 BatchIndex={BatchIndex} TotalBatches={TotalBatches}",
                batchIndex,
                totalBatches);
            progressReporter.ReportLog($"✗ 批次 {batchIndex}/{totalBatches} 已取消");
            throw; // 重新抛出以取消其他任务
        }
        catch (Exception ex)
        {
            // 批次级错误处理 - 不影响其他批次
            progressReporter.ReportLog($"✗ 批次 {batchIndex}/{totalBatches} 失败: {ex.Message}");
            _logger.LogError(
                ex,
                "翻译批次失败 BatchIndex={BatchIndex} TotalBatches={TotalBatches}",
                batchIndex,
                totalBatches);

            // 标记批次中的所有项为失败
            await _applyItemUpdatesAsync(() =>
            {
                foreach (var group in batch)
                {
                    foreach (var item in group)
                    {
                        item.Status = "翻译失败";
                    }
                }
            });

            return false;
        }
    }

    /// <summary>
    /// 构建翻译源字典
    /// </summary>
    private Dictionary<string, string> BuildSourceDictionary(List<IGrouping<string, TranslationItem>> batch)
    {
        var dict = new Dictionary<string, string>();
        foreach (var group in batch)
        {
            // 使用原文作为 Key（需要去重）
            // 如果同一个原文出现在多个 TranslationItem 中，我们只需要翻译一次
            if (!dict.ContainsKey(group.Key))
            {
                dict[group.Key] = group.Key;
            }
        }
        return dict;
    }

    /// <summary>
    /// 应用翻译结果到 TranslationItem
    /// </summary>
    private int ApplyTranslations(List<IGrouping<string, TranslationItem>> batch, Dictionary<string, string> translations)
    {
        var missingCount = 0;
        foreach (var group in batch)
        {
            if (translations.TryGetValue(group.Key, out string? translatedText) &&
                !string.IsNullOrWhiteSpace(translatedText))
            {
                foreach (var item in group)
                {
                    item.TranslatedText = translatedText;
                    item.Status = "已翻译";
                }
            }
            else
            {
                missingCount++;
                // 翻译结果中没有该原文或结果为空
                foreach (var item in group)
                {
                    item.Status = "未翻译";
                }
            }
        }

        return missingCount;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (_ownsLlmService)
        {
            _llmService.Dispose();
        }
        _disposed = true;
    }
}
