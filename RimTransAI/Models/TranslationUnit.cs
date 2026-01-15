namespace RimTransAI.Models;

/// <summary>
/// 表示一个翻译单元
/// </summary>
public class TranslationUnit
{
    /// <summary>
    /// 完整的 Key（例如 "ThingDef.Gun_Revolver.label"）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Def 类型（例如 "ThingDef", "RecipeDef"）
    /// 用于确定 DefInjected 的子文件夹名称
    /// </summary>
    public string DefType { get; set; } = string.Empty;

    /// <summary>
    /// 原始文本内容
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// 源文件路径
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// 上下文信息（XML路径提示）
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// 版本号（如 1.4, 1.5）
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
