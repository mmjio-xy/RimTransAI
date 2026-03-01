using IconPacks.Avalonia.Material;

namespace RimTransAI.Models;

/// <summary>
/// 来源图标下拉项。
/// </summary>
public sealed class SourceIconOption
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required PackIconMaterialKind Kind { get; init; }
}
