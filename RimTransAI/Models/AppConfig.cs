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

    // 提示词配置
    public string CustomPrompt { get; set; } = "";         // 自定义提示词
    public bool UseCustomPrompt { get; set; } = false;     // 是否使用自定义提示词
    public string PromptTemplateName { get; set; } = "Default"; // 预设模板名称

    // 多线程翻译配置
    public bool EnableMultiThreadTranslation { get; set; } = false; // 是否启用多线程翻译
    public int MaxThreads { get; set; } = 4; // 最大并发线程数（1-10）
    public int ThreadIntervalMs { get; set; } = 100; // 并发请求间隔（毫秒）

    // ========== 备份配置 ==========
    /// <summary>
    /// 是否启用自动备份
    /// </summary>
    public bool EnableAutoBackup { get; set; } = true;

    /// <summary>
    /// 备份存储目录（空则使用默认 AppData/RimTransAI/Backups）
    /// </summary>
    public string BackupDirectory { get; set; } = "";

    /// <summary>
    /// 最大备份数量（0 表示不限制）
    /// </summary>
    public int MaxBackupCount { get; set; } = 10;

    /// <summary>
    /// 备份压缩级别（0: Fastest, 1: Optimal, 2: SmallestSize）
    /// </summary>
    public int BackupCompressionLevel { get; set; } = 1;
}