using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.Material;

namespace RimTransAI.Models;

/// <summary>
/// 工作区中的单个 Mod 项。
/// </summary>
public partial class WorkspaceModItem : ObservableObject
{
    public string SourceFolderId { get; init; } = string.Empty;
    public string SourceDisplayName { get; init; } = string.Empty;
    public string SourceIconKey { get; init; } = string.Empty;
    public PackIconMaterialKind SourceIconKind { get; init; } = PackIconMaterialKind.Folder;

    public string ModPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;

    [ObservableProperty]
    private string _status = "未加载";

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private string _lastScanAt = "-";
}
