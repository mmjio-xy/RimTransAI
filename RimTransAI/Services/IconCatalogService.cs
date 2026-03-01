using System;
using System.Collections.Generic;
using System.Linq;
using IconPacks.Avalonia.Material;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 提供来源图标选项与兜底映射逻辑。
/// </summary>
public class IconCatalogService
{
    private static readonly SourceIconOption[] SourceIconPreset =
    {
        new() { Key = nameof(PackIconMaterialKind.Folder), Label = "默认文件夹", Kind = PackIconMaterialKind.Folder },
        new() { Key = nameof(PackIconMaterialKind.FolderOutline), Label = "文件夹(线框)", Kind = PackIconMaterialKind.FolderOutline },
        new() { Key = nameof(PackIconMaterialKind.FolderCog), Label = "文件夹设置", Kind = PackIconMaterialKind.FolderCog },
        new() { Key = nameof(PackIconMaterialKind.GamepadVariant), Label = "游戏", Kind = PackIconMaterialKind.GamepadVariant },
        new() { Key = nameof(PackIconMaterialKind.HammerWrench), Label = "开发", Kind = PackIconMaterialKind.HammerWrench },
        new() { Key = nameof(PackIconMaterialKind.RocketLaunch), Label = "发布", Kind = PackIconMaterialKind.RocketLaunch },
        new() { Key = nameof(PackIconMaterialKind.Database), Label = "数据", Kind = PackIconMaterialKind.Database },
        new() { Key = nameof(PackIconMaterialKind.Cloud), Label = "云端", Kind = PackIconMaterialKind.Cloud }
    };

    public IReadOnlyList<SourceIconOption> GetSourceIconOptions()
    {
        return SourceIconPreset;
    }

    public PackIconMaterialKind ResolveSourceIconKind(string? iconKey, string stableSeed)
    {
        if (!string.IsNullOrWhiteSpace(iconKey)
            && Enum.TryParse(iconKey, true, out PackIconMaterialKind explicitKind))
        {
            return explicitKind;
        }

        if (string.IsNullOrWhiteSpace(stableSeed))
        {
            return PackIconMaterialKind.Folder;
        }

        var hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(stableSeed));
        var index = hash % SourceIconPreset.Length;
        return SourceIconPreset[index].Kind;
    }

    public string ResolveIconKey(string? iconKey, string stableSeed)
    {
        if (!string.IsNullOrWhiteSpace(iconKey)
            && Enum.TryParse(iconKey, true, out PackIconMaterialKind explicitKind))
        {
            return explicitKind.ToString();
        }

        return ResolveSourceIconKind(iconKey, stableSeed).ToString();
    }

    public SourceIconOption GetOptionByKey(string? iconKey, string stableSeed)
    {
        var resolvedKey = ResolveIconKey(iconKey, stableSeed);
        var option = SourceIconPreset.FirstOrDefault(x =>
            string.Equals(x.Key, resolvedKey, StringComparison.OrdinalIgnoreCase));

        if (option != null)
        {
            return option;
        }

        return new SourceIconOption
        {
            Key = resolvedKey,
            Label = resolvedKey,
            Kind = ResolveSourceIconKind(resolvedKey, stableSeed)
        };
    }
}
