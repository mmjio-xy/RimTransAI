using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 多线程翻译服务
/// </summary>
public class MultiThreadedTranslationService : IDisposable
{
    private readonly LlmService _llmService = new();
    private bool _disposed;

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
    /// <returns>完成的批次数</returns>
    public async Task<int> ExecuteBatchesAsync(
        BatchingService.BatchResult batchResult,
        ConcurrencyManager concurrencyManager,
        ThreadSafeProgressReporter progressReporter,
        string apiKey,
        string apiUrl,
        string model,
        string targetLang,
        string? customPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MultiThreadedTranslationService));

        if (batchResult.Batches.Count == 0)
            return 0;

        // 创建所有批次任务
        var tasks = batchResult.Batches
            .Select((batch, index) => ProcessBatchAsync(
                batch,
                index + 1,
                batchResult.TotalBatches,
                concurrencyManager,
                progressReporter,
                apiKey,
                apiUrl,
                model,
                targetLang,
                customPrompt,
                cancellationToken))
            .ToList();

        // 等待所有任务完成
        await Task.WhenAll(tasks);

        return batchResult.TotalBatches;
    }

    /// <summary>
    /// 处理单个批次
    /// </summary>
    private async Task ProcessBatchAsync(
        List<IGrouping<string, TranslationItem>> batch,
        int batchIndex,
        int totalBatches,
        ConcurrencyManager concurrencyManager,
        ThreadSafeProgressReporter progressReporter,
        string apiKey,
        string apiUrl,
        string model,
        string targetLang,
        string? customPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            // 报告批次开始
            progressReporter.ReportLog($"开始处理批次 {batchIndex}/{totalBatches}，包含 {batch.Count} 个翻译组");

            // 使用并发控制器执行翻译
            var translations = await concurrencyManager.ExecuteAsync(async ct => await _llmService.TranslateBatchAsync(
                apiKey,
                BuildSourceDictionary(batch),
                apiUrl,
                model,
                targetLang,
                customPrompt), cancellationToken);

            // 应用翻译结果
            ApplyTranslations(batch, translations);

            // 报告批次完成
            progressReporter.ReportLog($"✓ 批次 {batchIndex}/{totalBatches} 完成，翻译 {batch.Count} 个文本");
        }
        catch (OperationCanceledException)
        {
            progressReporter.ReportLog($"✗ 批次 {batchIndex}/{totalBatches} 已取消");
            throw; // 重新抛出以取消其他任务
        }
        catch (Exception ex)
        {
            // 批次级错误处理 - 不影响其他批次
            progressReporter.ReportLog($"✗ 批次 {batchIndex}/{totalBatches} 失败: {ex.Message}");
            Logger.Error($"批次 {batchIndex}/{totalBatches} 翻译失败", ex);

            // 标记批次中的所有项为失败
            foreach (var group in batch)
            {
                foreach (var item in group)
                {
                    item.Status = "翻译失败";
                }
            }
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
    private void ApplyTranslations(List<IGrouping<string, TranslationItem>> batch, Dictionary<string, string> translations)
    {
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
                // 翻译结果中没有该原文或结果为空
                foreach (var item in group)
                {
                    item.Status = "未翻译";
                }
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _llmService?.Dispose();
        _disposed = true;
    }
}
