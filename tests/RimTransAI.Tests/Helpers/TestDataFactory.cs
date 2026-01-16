using RimTransAI.Models;

namespace RimTransAI.Tests.Helpers;

/// <summary>
/// 测试数据工厂
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            ApiUrl = "https://api.test.com/v1/chat/completions",
            ApiKey = "test-api-key",
            TargetModel = "test-model",
            TargetLanguage = "ChineseSimplified",
            AppTheme = "Light",
            AssemblyCSharpPath = "",
            DebugMode = false
        };
    }

    /// <summary>
    /// 创建翻译项
    /// </summary>
    public static TranslationItem CreateTranslationItem(
        string key = "TestDef.label",
        string original = "Test Item",
        string? translated = null,
        string status = "等待中")
    {
        return new TranslationItem
        {
            Key = key,
            OriginalText = original,
            TranslatedText = translated ?? "",
            Status = status,
            DefType = "ThingDef",
            Version = "1.5",
            FilePath = "Defs/Test.xml"
        };
    }
}
