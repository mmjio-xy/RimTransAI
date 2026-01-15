namespace RimTransAI.Models;

/// <summary>
/// 表示一个需要注入翻译的项
/// </summary>
public class InjectionItem
{
    /// <summary>
    /// Def 的名称（从 defName 元素获取）
    /// </summary>
    public string DefName { get; set; } = string.Empty;

    /// <summary>
    /// 字段名称
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 原始文本内容
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// 完整的 Key（用于 DefInjected）
    /// </summary>
    public string FullKey => $"{DefName}.{FieldName}";
}
