using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimTransAI.Services;

/// <summary>
/// Token 估算工具类
/// 用于估算文本的 Token 数量，以便智能分批翻译
/// </summary>
public static class TokenEstimator
{
    // 中文字符正则（包括中日韩统一表意文字）
    private static readonly Regex CjkRegex = new Regex(
        @"[\u4e00-\u9fff\u3400-\u4dbf\u3000-\u303f\uff00-\uffef]",
        RegexOptions.Compiled);

    // JSON 结构开销：每个 key-value 对约 4 tokens（引号、冒号、逗号等）
    private const int JsonKeyValueOverhead = 4;

    // 安全边际系数（预留 20% 余量）
    private const double SafetyMargin = 0.8;

    /// <summary>
    /// 估算单条文本的 Token 数量
    /// </summary>
    /// <param name="text">待估算的文本</param>
    /// <returns>估算的 Token 数量</returns>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int totalTokens = 0;

        // 统计中文字符数量（约 1.5 token/字符）
        var cjkMatches = CjkRegex.Matches(text);
        int cjkCount = cjkMatches.Count;
        totalTokens += (int)(cjkCount * 1.5);

        // 统计非中文字符数量（约 0.25 token/字符，即 4 字符 = 1 token）
        int nonCjkCount = text.Length - cjkCount;
        totalTokens += (int)Math.Ceiling(nonCjkCount / 4.0);

        return Math.Max(1, totalTokens);
    }

    /// <summary>
    /// 估算批量翻译请求的总 Token 数量
    /// 包含 JSON 结构开销
    /// </summary>
    /// <param name="batch">Key-Value 字典（Key: 翻译键, Value: 原文）</param>
    /// <returns>估算的总 Token 数量</returns>
    public static int EstimateBatchTokens(Dictionary<string, string> batch)
    {
        if (batch == null || batch.Count == 0)
            return 0;

        int totalTokens = 0;

        foreach (var kvp in batch)
        {
            // Key 的 Token 数
            totalTokens += EstimateTokens(kvp.Key);
            // Value 的 Token 数
            totalTokens += EstimateTokens(kvp.Value);
            // JSON 结构开销
            totalTokens += JsonKeyValueOverhead;
        }

        // 额外的 JSON 包装开销（大括号、数组等）
        totalTokens += 10;

        return totalTokens;
    }

    /// <summary>
    /// 计算安全的 Token 限制（应用安全边际）
    /// </summary>
    /// <param name="maxTokens">原始 Token 限制</param>
    /// <returns>应用安全边际后的限制</returns>
    public static int GetSafeTokenLimit(int maxTokens)
    {
        return (int)(maxTokens * SafetyMargin);
    }

    /// <summary>
    /// 判断文本是否为超长文本（单条超过安全限制的 50%）
    /// </summary>
    /// <param name="text">待检查的文本</param>
    /// <param name="maxTokensPerBatch">每批次最大 Token 数</param>
    /// <returns>是否为超长文本</returns>
    public static bool IsOversizedText(string text, int maxTokensPerBatch)
    {
        int tokens = EstimateTokens(text);
        int safeLimit = GetSafeTokenLimit(maxTokensPerBatch);
        return tokens > safeLimit / 2;
    }
}
