namespace RimTransAI.Models;

/// <summary>
/// 仅用于读取未带 version 的旧 settings.json，绝不写回新格式。
/// </summary>
public sealed class LegacyApiSettings
{
    public string ApiUrl { get; set; } = "https://api.openai.com";
    public string ApiKey { get; set; } = string.Empty;
    public string TargetModel { get; set; } = "gpt-3.5-turbo";
    public bool AutoCompleteApiUrl { get; set; } = true;
    public int ApiRequestTimeoutSeconds { get; set; } = 480;
}
