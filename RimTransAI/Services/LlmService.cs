using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class LlmService : IDisposable
{
    private static readonly HttpClient SharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2) // 翻译可能会慢，超时设长一点
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
    string? customPrompt = null)
{
    if (sourceTexts.Count == 0) return new Dictionary<string, string>();

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
    var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
    request.Headers.Add("Authorization", $"Bearer {apiKey}");
    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    // 5. 解析响应 (这里稍微麻烦点，因为我们没定义 Response 类，可以暂时用 JsonNode)
    // 1. 先作为普通字符串读取出来
    var rawResponse = await response.Content.ReadAsStringAsync();

    // 2. 使用生成的 AOT 上下文手动反序列化
    var jsonResponse = JsonSerializer.Deserialize(
        rawResponse, 
        AppJsonContext.Default.JsonObject
    );
    var content = jsonResponse?["choices"]?[0]?["message"]?["content"]?.ToString();

    if (string.IsNullOrEmpty(content)) return new Dictionary<string, string>();

    try
    {
        // 清理可能的 markdown 代码块标记（只在开头和结尾清理）
        content = content.Trim();
        if (content.StartsWith("```json"))
        {
            content = content.Substring(7); // 移除 "```json"
        }
        else if (content.StartsWith("```"))
        {
            content = content.Substring(3); // 移除 "```"
        }

        if (content.EndsWith("```"))
        {
            content = content.Substring(0, content.Length - 3); // 移除结尾的 "```"
        }

        content = content.Trim();

        // 使用 Context 反序列化结果字典
        return JsonSerializer.Deserialize(content, AppJsonContext.Default.DictionaryStringString)
               ?? new Dictionary<string, string>();
    }
    catch (JsonException ex)
    {
        Logger.Error($"JSON解析失败: {ex.Message}");
        Logger.Error($"响应内容: {content}");
        return new Dictionary<string, string>();
    }
    catch (Exception ex)
    {
        Logger.Error($"翻译结果处理失败: {ex.GetType().Name} - {ex.Message}");
        return new Dictionary<string, string>();
    }
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