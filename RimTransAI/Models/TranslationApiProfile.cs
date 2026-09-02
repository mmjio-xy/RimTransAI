using System;
using System.Collections.Generic;
using System.Linq;

namespace RimTransAI.Models;

/// <summary>
/// 单个 OpenAI 兼容翻译 API 的连接与模型配置。
/// </summary>
public sealed class TranslationApiProfile
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool AutoCompleteApiUrl { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 480;
    public List<TranslationApiModel> Models { get; set; } = new();

    public static TranslationApiProfile CreateDefault() => new()
    {
        Name = "默认 API",
        ApiUrl = "https://api.openai.com",
        Models = [new TranslationApiModel { ModelId = "gpt-3.5-turbo" }]
    };

    public TranslationApiProfile Clone() => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        ApiUrl = ApiUrl,
        ApiKey = ApiKey,
        AutoCompleteApiUrl = AutoCompleteApiUrl,
        RequestTimeoutSeconds = RequestTimeoutSeconds,
        Models = (Models ?? []).Select(x => x.Clone()).ToList()
    };
}

/// <summary>
/// API 下可参与翻译的实际模型标识。
/// </summary>
public sealed class TranslationApiModel
{
    public string ModelId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public TranslationApiModel Clone() => new()
    {
        ModelId = ModelId,
        IsEnabled = IsEnabled
    };
}
