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

    // 翻译后的中文
    [ObservableProperty] 
    private string _translatedText = string.Empty;

    // 当前状态 (例如: 等待中, 翻译中, 已完成)
    [ObservableProperty] 
    private string _status = "等待中";
    
    // 新增：所属版本 (例如 "1.4", "1.5", "Common")
    public string Version { get; set; } = string.Empty;

    // 来源文件路径 (用于最后生成文件)
    public string FilePath { get; set; } = string.Empty;
}