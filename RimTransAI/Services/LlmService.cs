using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace RimTransAI.Services;

public class LlmService
{
    private readonly HttpClient _httpClient;
    
    // 如果你用的是其他模型（如 DeepSeek/Ollama），在这里修改 BaseUrl
    private const string BaseUrl = "https://api.deepseek.com/chat/completions"; 
    // private const string BaseUrl = "https://api.deepseek.com/chat/completions";

    public LlmService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(2); // 翻译可能会慢，超时设长一点
    }

    /// <summary>
    /// 批量翻译
    /// </summary>
    /// <param name="apiKey">API Key</param>
    /// <param name="sourceTexts">Key: 原文ID, Value: 英文原文</param>
    /// <param name="targetLang">目标语言</param>
    /// <returns>Key: 原文ID, Value: 中文译文</returns>
    public async Task<Dictionary<string, string>> TranslateBatchAsync(string apiKey, Dictionary<string, string> sourceTexts, string targetLang = "Simplified Chinese")
    {
        if (sourceTexts.Count == 0) return new Dictionary<string, string>();

        // 1. 构造 System Prompt (核心！告诉 AI 它是谁，以及必须遵守的规则)
        var systemPrompt = $@"You are a professional translator for the game RimWorld.
Target Language: {targetLang}.
Rules:
1. Preserve all XML tags (e.g., <li>, <b>, <color>), variables (e.g., {{0}}, {{HUMAN_label}}), and file paths exactly as they are.
2. Translate the content naturally, fitting the sci-fi/survival context of RimWorld.
3. Input is a JSON object. Output MUST be a valid JSON object with the same keys.
4. Do NOT output markdown code blocks (```json), just the raw JSON string.
5. If a value is a technical string (like a defName 'Gun_Revolver'), do not translate it unless it's a label.";

        // 2. 准备请求体
        // 我们把 Dictionary 转成 JSON 字符串发给 AI
        var userContent = JsonSerializer.Serialize(sourceTexts);

        var requestBody = new
        {
            model = "deepseek-chat", // 建议用 gpt-4o-mini 或 deepseek-chat，性价比高
            // model = "deepseek-chat", 
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            temperature = 0.3, // 低温度保证格式稳定
            response_format = new { type = "json_object" } // 强制 JSON 模式 (OpenAI 特性，其他模型可能需移除)
        };

        // 3. 发送请求
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = JsonContent.Create(requestBody);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // 4. 解析响应
        var jsonResponse = await response.Content.ReadFromJsonAsync<JsonObject>();
        var content = jsonResponse?["choices"]?[0]?["message"]?["content"]?.ToString();

        if (string.IsNullOrEmpty(content)) return new Dictionary<string, string>();

        // 5. 反序列化回 Dictionary
        try 
        {
            // 清理可能存在的 Markdown 标记 (以防万一模型不听话)
            content = content.Replace("```json", "").Replace("```", "").Trim();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(content) ?? new Dictionary<string, string>();
        }
        catch
        {
            // 如果 JSON 解析失败，返回空字典，避免程序崩溃
            Console.WriteLine($"JSON Parse Error. Content: {content}");
            return new Dictionary<string, string>();
        }
    }
}