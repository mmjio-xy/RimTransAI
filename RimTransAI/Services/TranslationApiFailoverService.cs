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
/// 以 API、模型列表的既定顺序执行翻译，并在失败时切换到下一个候选项。
/// </summary>
public sealed class TranslationApiFailoverService
{
    private readonly LlmService _llmService;
    private readonly ILogger<TranslationApiFailoverService> _logger;

    public TranslationApiFailoverService(
        LlmService llmService,
        ILogger<TranslationApiFailoverService>? logger = null)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _logger = logger ?? NullLogger<TranslationApiFailoverService>.Instance;
    }

    public static bool HasEnabledTranslationTarget(IEnumerable<TranslationApiProfile>? apiProfiles) =>
        apiProfiles?.Any(api => api.IsEnabled &&
                                (api.Models?.Any(model => model.IsEnabled) ?? false)) == true;

    public async Task<Dictionary<string, string>> TranslateBatchAsync(
        IEnumerable<TranslationApiProfile> apiProfiles,
        Dictionary<string, string> sourceTexts,
        string targetLanguage,
        string? customPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apiProfiles);
        ArgumentNullException.ThrowIfNull(sourceTexts);

        var candidates = ExpandEnabledCandidates(apiProfiles).ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("未配置可用的 API 和模型，请在程序设置中启用至少一个模型。");
        }

        var failedApiNames = new List<string>();
        var knownFailedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var translations = await _llmService.TranslateBatchAsync(
                    candidate.ApiKey,
                    sourceTexts,
                    candidate.ApiUrl,
                    candidate.ModelId,
                    targetLanguage,
                    customPrompt,
                    candidate.RequestTimeoutSeconds,
                    candidate.AutoCompleteApiUrl,
                    cancellationToken);

                EnsureCompleteResponse(sourceTexts, translations);
                return translations;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (knownFailedApiNames.Add(candidate.ApiName))
                {
                    failedApiNames.Add(candidate.ApiName);
                }

                _logger.LogWarning(
                    ex,
                    "翻译 API 调用失败，准备尝试下一个候选项 ApiName={ApiName} ModelId={ModelId}",
                    candidate.ApiName,
                    candidate.ModelId);
            }
        }

        throw new TranslationApiFailoverException(failedApiNames);
    }

    private static IEnumerable<TranslationApiCandidate> ExpandEnabledCandidates(
        IEnumerable<TranslationApiProfile> apiProfiles)
    {
        foreach (var api in apiProfiles)
        {
            if (!api.IsEnabled)
            {
                continue;
            }

            var apiName = string.IsNullOrWhiteSpace(api.Name) ? "未命名 API" : api.Name;
            foreach (var model in api.Models ?? [])
            {
                if (!model.IsEnabled)
                {
                    continue;
                }

                yield return new TranslationApiCandidate(
                    apiName,
                    api.ApiUrl,
                    api.ApiKey,
                    api.AutoCompleteApiUrl,
                    api.RequestTimeoutSeconds,
                    model.ModelId);
            }
        }
    }

    private static void EnsureCompleteResponse(
        IReadOnlyDictionary<string, string> sourceTexts,
        IReadOnlyDictionary<string, string> translations)
    {
        var missingCount = sourceTexts.Count(pair =>
            !translations.TryGetValue(pair.Key, out var translation) ||
            string.IsNullOrWhiteSpace(translation));

        if (missingCount > 0)
        {
            throw new InvalidOperationException($"API 返回结果不完整，缺少 {missingCount} 项翻译。");
        }
    }

    private sealed record TranslationApiCandidate(
        string ApiName,
        string ApiUrl,
        string ApiKey,
        bool AutoCompleteApiUrl,
        int RequestTimeoutSeconds,
        string ModelId);
}

public sealed class TranslationApiFailoverException : Exception
{
    public TranslationApiFailoverException(IReadOnlyList<string> failedApiNames)
        : base($"API 调用失败：【{string.Join("、", failedApiNames)}】")
    {
        FailedApiNames = failedApiNames;
    }

    public IReadOnlyList<string> FailedApiNames { get; }
}
