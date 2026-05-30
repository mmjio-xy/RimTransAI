using System;
using System.Collections.Generic;
using System.ClientModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

namespace RimTransAI.Services;

public class LlmService : IDisposable
{
    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const string V1Path = "/v1";

    /// <summary>
    /// 批量翻译
    /// </summary>
    /// <param name="apiKey">API Key</param>
    /// <param name="sourceTexts">Key: 原文ID, Value: 英文原文</param>
    /// <param name="apiUrl">API 地址（Base URL 或完整 chat/completions 路径均可）</param>
    /// <param name="model">模型名称</param>
    /// <param name="targetLang">目标语言</param>
    /// <param name="customPrompt">自定义提示词（可选）</param>
    /// <param name="requestTimeoutSeconds">请求超时秒数</param>
    /// <param name="autoCompleteApiUrl"></param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Dictionary<string, string>> TranslateBatchAsync(
        string apiKey,
        Dictionary<string, string> sourceTexts,
        string apiUrl,
        string model,
        string targetLang = "Simplified Chinese",
        string? customPrompt = null,
        int requestTimeoutSeconds = 480,
        bool autoCompleteApiUrl = true,
        CancellationToken cancellationToken = default)
    {
        if (sourceTexts.Count == 0)
            return new Dictionary<string, string>();

        var endpoint = BuildEndpoint(apiUrl, autoCompleteApiUrl);
        var timeoutSeconds = Math.Clamp(requestTimeoutSeconds, 30, 1800);

        Logger.Debug($"LLM 请求 — Endpoint: {endpoint} | 模型: {model} | 条目数: {sourceTexts.Count} | 超时: {timeoutSeconds}s");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint
        };

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        var chatClient = client.GetChatClient(model);

        var systemPrompt = !string.IsNullOrWhiteSpace(customPrompt)
            ? customPrompt.Replace("{targetLang}", targetLang)
            : $@"You are a professional translator for RimWorld. Target: {targetLang}.
 Rules: Preserve XML tags, variables like {{0}}, and paths. Input/Output is JSON.";

        var userContent = JsonSerializer.Serialize(sourceTexts, AppJsonContext.Default.DictionaryStringString);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userContent)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.3f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        try
        {
            var completion = await chatClient.CompleteChatAsync(messages, chatOptions, timeoutCts.Token);
            var content = completion.Value.Content[0].Text;

            if (string.IsNullOrEmpty(content))
            {
                Logger.Warning("LLM 返回空 content");
                return new Dictionary<string, string>();
            }

            Logger.Debug($"LLM 响应完成 — 模型: {completion.Value.Model} | Tokens: {completion.Value.Usage?.TotalTokenCount}");

            // 清理可能的 markdown 代码块标记
            content = content.Trim();
            if (content.StartsWith("```json"))
                content = content[7..];
            else if (content.StartsWith("```"))
                content = content[3..];
            if (content.EndsWith("```"))
                content = content[..^3];
            content = content.Trim();

            var result = JsonSerializer.Deserialize(content, AppJsonContext.Default.DictionaryStringString)
                         ?? new Dictionary<string, string>();
            Logger.Debug($"LLM 翻译结果: {result.Count}/{sourceTexts.Count} 条成功");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            Logger.Warning($"LLM 请求超时（{timeoutSeconds} 秒）");
            throw new TimeoutException($"API 请求超时（{timeoutSeconds} 秒）");
        }
        catch (Exception ex)
        {
            Logger.Error($"LLM 翻译失败: {ex.GetType().Name} - {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 规范化 API 地址，允许用户只填写 Base URL。
    /// 规则：如果 URL 已以 /chat/completions 结尾则不拼接；
    /// 如果以 /v1 结尾则补全 /chat/completions；
    /// 否则补齐标准路径 /v1/chat/completions。
    /// </summary>
    public static string NormalizeApiUrl(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            return apiUrl;

        var normalized = apiUrl.Trim().TrimEnd('/');

        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (normalized.EndsWith(V1Path, StringComparison.OrdinalIgnoreCase))
            return normalized + "/chat/completions";

        return normalized + ChatCompletionsPath;
    }

    /// <summary>
    /// 将用户输入的 API 地址转换为 SDK 所需的 Endpoint。
    /// 当 autoComplete 为 true 时，自动补全 /v1（兼容 OpenAI/DeepSeek 等标准 API）；
    /// 为 false 时原样使用（适用于自定义代理等非标准路径）。
    /// </summary>
    private static Uri BuildEndpoint(string apiUrl, bool autoComplete)
    {
        var url = apiUrl.Trim().TrimEnd('/');

        // 移除可能存在的 /chat/completions 后缀（SDK 会自动追加）
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/chat/completions".Length];

        if (!autoComplete)
            return new Uri(url);

        // 自动补全：确保以 /v1 结尾
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url += "/v1";

        return new Uri(url);
    }

    public void Dispose()
    {
    }
}
