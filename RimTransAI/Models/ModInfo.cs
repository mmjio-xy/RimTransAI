using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace RimTransAI.Models;

/// <summary>
/// Mod 元数据信息（对应 About.xml）
/// </summary>
public partial class ModInfo : ObservableObject
{
    // ========== 基础信息 ==========

    // Mod 名称
    [ObservableProperty]
    private string _name = string.Empty;

    // 作者
    [ObservableProperty]
    private string _author = string.Empty;

    // 描述
    [ObservableProperty]
    private string _description = string.Empty;

    // 支持的游戏版本列表（例如 ["1.4", "1.5"]）
    [ObservableProperty]
    private List<string> _supportedVersions = new();

    // ========== 扩展信息 ==========

    // 包 ID（例如 "author.modname"）
    [ObservableProperty]
    private string _packageId = string.Empty;

    // 项目主页 URL
    [ObservableProperty]
    private string _url = string.Empty;

    // ========== Mod 路径 ==========

    // Mod 文件夹路径（用于显示和打开文件管理器）
    [ObservableProperty]
    private string _modFolderPath = string.Empty;

    // ========== 依赖关系 ==========

    // Mod 依赖列表
    [ObservableProperty]
    private List<ModDependency> _modDependencies = new();

    // ========== 图片路径 ==========

    // 预览图路径（About/Preview.png）
    [ObservableProperty]
    private string _previewImagePath = string.Empty;
}

/// <summary>
/// Mod 依赖项
/// </summary>
public class ModDependency
{
    // 依赖的包 ID
    public string PackageId { get; set; } = string.Empty;

    // 依赖的显示名称
    public string DisplayName { get; set; } = string.Empty;
}
