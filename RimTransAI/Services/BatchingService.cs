using System;
using System.Collections.Generic;
using System.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 智能分批服务
/// 根据 Token 估算将翻译项分成合适大小的批次
/// </summary>
public class BatchingService
{
    /// <summary>
    /// 分批结果
    /// </summary>
    public class BatchResult
    {
        /// <summary>
        /// 分批后的翻译组列表
        /// </summary>
        public List<List<IGrouping<string, TranslationItem>>> Batches { get; set; } = new();

        /// <summary>
        /// 每个批次的估算 Token 数
        /// </summary>
        public List<int> BatchTokenCounts { get; set; } = new();

        /// <summary>
        /// 总批次数
        /// </summary>
        public int TotalBatches => Batches.Count;

        /// <summary>
        /// 超长文本批次数（单条成批）
        /// </summary>
        public int OversizedBatches { get; set; }
    }

    /// <summary>
    /// 创建智能分批
    /// </summary>
    /// <param name="groups">按原文分组的翻译项</param>
    /// <param name="maxTokensPerBatch">每批次最大 Token 数（默认 3000）</param>
    /// <param name="minItemsPerBatch">每批次最少条目数（默认 5）</param>
    /// <param name="maxItemsPerBatch">每批次最多条目数（默认 50）</param>
    /// <returns>分批结果</returns>
    public BatchResult CreateBatches(
        List<IGrouping<string, TranslationItem>> groups,
        int maxTokensPerBatch = 3000,
        int minItemsPerBatch = 5,
        int maxItemsPerBatch = 50)
    {
        var result = new BatchResult();

        if (groups == null || groups.Count == 0)
            return result;

        // 获取安全 Token 限制
        int safeTokenLimit = TokenEstimator.GetSafeTokenLimit(maxTokensPerBatch);

        // 分离超长文本和普通文本
        var oversizedGroups = new List<IGrouping<string, TranslationItem>>();
        var normalGroups = new List<IGrouping<string, TranslationItem>>();

        foreach (var group in groups)
        {
            if (TokenEstimator.IsOversizedText(group.Key, maxTokensPerBatch))
            {
                oversizedGroups.Add(group);
            }
            else
            {
                normalGroups.Add(group);
            }
        }

        // 处理超长文本：每条单独成批
        foreach (var group in oversizedGroups)
        {
            result.Batches.Add(new List<IGrouping<string, TranslationItem>> { group });
            result.BatchTokenCounts.Add(TokenEstimator.EstimateTokens(group.Key));
            result.OversizedBatches++;
        }

        // 处理普通文本：按 Token 数智能分批
        CreateNormalBatches(normalGroups, safeTokenLimit, minItemsPerBatch, maxItemsPerBatch, result);

        return result;
    }

    /// <summary>
    /// 处理普通文本的分批逻辑
    /// </summary>
    private void CreateNormalBatches(
        List<IGrouping<string, TranslationItem>> groups,
        int safeTokenLimit,
        int minItemsPerBatch,
        int maxItemsPerBatch,
        BatchResult result)
    {
        if (groups.Count == 0)
            return;

        // 按文本长度排序（短文本优先，便于聚合）
        var sortedGroups = groups.OrderBy(g => g.Key.Length).ToList();

        var currentBatch = new List<IGrouping<string, TranslationItem>>();
        int currentTokens = 0;

        foreach (var group in sortedGroups)
        {
            // 估算当前项的 Token 数（包含 JSON 开销）
            int itemTokens = TokenEstimator.EstimateTokens(group.Key) + 4;

            // 判断是否需要开启新批次
            bool shouldStartNewBatch = false;

            if (currentBatch.Count > 0)
            {
                // 条件1：加入后超过 Token 限制
                if (currentTokens + itemTokens > safeTokenLimit)
                    shouldStartNewBatch = true;

                // 条件2：已达到最大条目数
                if (currentBatch.Count >= maxItemsPerBatch)
                    shouldStartNewBatch = true;
            }

            if (shouldStartNewBatch)
            {
                // 保存当前批次
                result.Batches.Add(currentBatch);
                result.BatchTokenCounts.Add(currentTokens);

                // 开启新批次
                currentBatch = new List<IGrouping<string, TranslationItem>>();
                currentTokens = 0;
            }

            // 添加到当前批次
            currentBatch.Add(group);
            currentTokens += itemTokens;
        }

        // 保存最后一个批次
        if (currentBatch.Count > 0)
        {
            result.Batches.Add(currentBatch);
            result.BatchTokenCounts.Add(currentTokens);
        }
    }
}