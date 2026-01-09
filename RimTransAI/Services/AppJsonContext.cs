using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RimTransAI.Models;

// 引用 Models

namespace RimTransAI.Services;

[JsonSerializable(typeof(AppConfig))]                    // 设置文件
[JsonSerializable(typeof(Dictionary<string, string>))]   // 翻译内容
[JsonSerializable(typeof(LlmRequest))]                   // LLM 请求体
[JsonSerializable(typeof(JsonObject))]
public partial class AppJsonContext : JsonSerializerContext
{}