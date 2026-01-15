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
}