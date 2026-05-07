using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class LlmService : IDisposable
{
    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const string V1Path = "/v1";
    private const int DefaultRequestTimeoutSeconds = 480;

    private static readonly HttpClient SharedHttpClient = new HttpClient
    {
        // 统一改为由每次请求的 CancellationToken 控制超时，避免固定 2 分钟导致长批次频繁超时
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly HttpClient _httpClient;
    private bool _disposed = false;

    // 如果你用的是其他模型（如 DeepSeek/Ollama），在这里修改 BaseUrl
    // private const string BaseUrl = "https://api.deepseek.com/chat/completions";
    // private const string BaseUrl = "https://api.deepseek.com/chat/completions";

    public LlmService()
    {
        // 使用共享的静态 HttpClient 实例，避免端口耗尽问题
        _httpClient = SharedHttpClient;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 批量翻译
    /// </summary>
    /// <param name="apiKey">API Key</param>
    /// <param name="sourceTexts">Key: 原文ID, Value: 英文原文</param>
    /// <param name="targetLang">目标语言</param>
    /// <param name="customPrompt">自定义提示词（可选，默认使用内置提示词）</param>
    /// <returns>Key: 原文ID, Value: 中文译文</returns>
    public async Task<Dictionary<string, string>> TranslateBatchAsync(
    string apiKey,
    Dictionary<string, string> sourceTexts,
    string apiUrl,
    string model,
    string targetLang = "Simplified Chinese",
    string? customPrompt = null,
    int requestTimeoutSeconds = DefaultRequestTimeoutSeconds,
    CancellationToken cancellationToken = default)
    {
        if (sourceTexts.Count == 0) return new Dictionary<string, string>();

        var requestUrl = NormalizeApiUrl(apiUrl);
        Logger.Debug($"LLM 请求 — URL: {requestUrl} | 模型: {model} | 条目数: {sourceTexts.Count} | 超时: {requestTimeoutSeconds}s");

        var timeoutSeconds = Math.Clamp(requestTimeoutSeconds, 30, 1800);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // 1. 序列化 User Content (这是一个 Dictionary)
        // 使用 Context 序列化字典
        var userContent = JsonSerializer.Serialize(sourceTexts, AppJsonContext.Default.DictionaryStringString);

        // 提示词选择逻辑：自定义提示词优先
        var systemPrompt = !string.IsNullOrWhiteSpace(customPrompt)
            ? customPrompt.Replace("{targetLang}", targetLang)
            : $@"You are a professional translator for RimWorld. Target: {targetLang}.
 Rules: Preserve XML tags, variables like {{0}}, and paths. Input/Output is JSON.";

        // 2. 构造请求对象 (使用我们新建的 LlmRequest 类，替代匿名对象)
        var requestBodyObj = new LlmRequest
        {
            model = model,
            messages = new List<LlmMessage>
            {
                new LlmMessage { role = "system", content = systemPrompt },
                new LlmMessage { role = "user", content = userContent }
            },
            temperature = 0.3,
            response_format = new LlmResponseFormat { type = "json_object" }
        };

        // 3. 序列化请求体 (使用 Context 序列化 LlmRequest)
        var jsonBody = JsonSerializer.Serialize(requestBodyObj, AppJsonContext.Default.LlmRequest);

        // 4. 发送请求
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        Logger.Debug($"LLM 请求体大小: {jsonBody.Length} 字节");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token);
            Logger.Debug($"LLM 响应 — 状态码: {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            Logger.Warning($"LLM 请求超时（{timeoutSeconds} 秒）");
            throw new TimeoutException($"API 请求超时（{timeoutSeconds} 秒）");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                Logger.Error($"LLM HTTP {(int)response.StatusCode}: {errorBody}");
                response.EnsureSuccessStatusCode();
            }

            // 5. 解析响应
            var rawResponse = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            Logger.Debug($"LLM 响应体大小: {rawResponse.Length} 字节");

            // 2. 使用生成的 AOT 上下文手动反序列化
            var jsonResponse = JsonSerializer.Deserialize(
                rawResponse,
                AppJsonContext.Default.JsonObject
            );
            var content = jsonResponse?["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrEmpty(content))
            {
                Logger.Warning("LLM 返回空 content — API 可能拒绝了请求或模型不支持 JSON 输出");
                return new Dictionary<string, string>();
            }

            try
            {
                // 清理可能的 markdown 代码块标记（只在开头和结尾清理）
                content = content.Trim();
                if (content.StartsWith("```json"))
                {
                    content = content.Substring(7);
                }
                else if (content.StartsWith("```"))
                {
                    content = content.Substring(3);
                }

                if (content.EndsWith("```"))
                {
                    content = content.Substring(0, content.Length - 3);
                }

                content = content.Trim();

                // 使用 Context 反序列化结果字典
                var result = JsonSerializer.Deserialize(content, AppJsonContext.Default.DictionaryStringString)
                             ?? new Dictionary<string, string>();
                Logger.Debug($"LLM 翻译结果: {result.Count}/{sourceTexts.Count} 条成功");
                return result;
            }
            catch (JsonException ex)
            {
                Logger.Error($"LLM JSON 解析失败: {ex.Message}");
                Logger.Error($"LLM 原始响应内容 ({content.Length} 字符): {content}");
                return new Dictionary<string, string>();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                Logger.Warning($"LLM 响应读取超时（{timeoutSeconds} 秒）");
                throw new TimeoutException($"API 响应读取超时（{timeoutSeconds} 秒）");
            }
            catch (Exception ex)
            {
                Logger.Error($"LLM 结果处理失败: {ex.GetType().Name} - {ex.Message}");
                Logger.Debug($"LLM 异常时原始内容: {content}");
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>
    /// 规范化 API 地址，允许用户只填写 Base URL。
    /// </summary>
    public static string NormalizeApiUrl(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            return apiUrl;

        var normalized = apiUrl.Trim().TrimEnd('/');

        if (normalized.EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (normalized.EndsWith(V1Path, StringComparison.OrdinalIgnoreCase))
            return normalized + "/chat/completions";

        return normalized + ChatCompletionsPath;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 注意：由于使用的是静态共享的 HttpClient，这里不需要释放
                // 如果将来改为每个实例独立的 HttpClient，需要在这里调用 _httpClient?.Dispose()
            }
            _disposed = true;
        }
    }
}
