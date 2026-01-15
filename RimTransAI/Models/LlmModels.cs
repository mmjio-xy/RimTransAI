using System.Collections.Generic;

namespace RimTransAI.Models;

// 定义发给 LLM 的请求结构
public class LlmRequest
{
    public string model { get; set; } = string.Empty;
    public List<LlmMessage> messages { get; set; } = new();
    public double temperature { get; set; } = 0.3;

    // 使用 object 是为了兼容不同模型的格式，或者直接定义具体类型
    // 这里为了简单，针对 OpenAI 的 JSON 模式
    public LlmResponseFormat response_format { get; set; } = new();
}

public class LlmMessage
{
    public string role { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
}

public class LlmResponseFormat
{
    public string type { get; set; } = "json_object";
}