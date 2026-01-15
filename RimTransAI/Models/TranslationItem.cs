using CommunityToolkit.Mvvm.ComponentModel;

namespace RimTransAI.Models;

public partial class TranslationItem : ObservableObject
{
    // XML中的键值，例如 "Gun_Revolver.label"
    [ObservableProperty] 
    private string _key = string.Empty;

    // 英文原文
    [ObservableProperty] 
    private string _originalText = string.Empty;

    // 当前状态 (例如: 等待中, 翻译中, 已完成)
    [ObservableProperty]
    private string _status = "等待中";

    // 翻译后的中文
    [ObservableProperty] 
    private string _translatedText = string.Empty;

    // 注意：以下属性不使用 ObservableProperty，因为它们在初始化后不会改变，不需要 UI 绑定通知

    // 所属版本 (例如 "1.4", "1.5", 空字符串表示根目录/通用版本)
    public string Version { get; set; } = string.Empty;

    // 来源文件路径 (用于最后生成文件时确定输出位置)
    public string FilePath { get; set; } = string.Empty;
}