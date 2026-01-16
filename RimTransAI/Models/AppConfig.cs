namespace RimTransAI.Models;

public class AppConfig
{
    // 默认值
    public string ApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string TargetModel { get; set; } = "gpt-3.5-turbo";
    public string TargetLanguage { get; set; } = "ChineseSimplified"; // 默认简中
    public string AppTheme { get; set; } = "Light";
    public string AssemblyCSharpPath { get; set; } = ""; // Assembly-CSharp.dll 路径
    public bool DebugMode { get; set; } = false; // 调试模式开关

    // 智能分批配置（高级用户可通过 settings.json 调整）
    public int MaxTokensPerBatch { get; set; } = 3000;  // 每批次最大 Token 数
    public int MinItemsPerBatch { get; set; } = 5;      // 每批次最少条目数
    public int MaxItemsPerBatch { get; set; } = 50;     // 每批次最多条目数
}