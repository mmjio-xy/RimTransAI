using System;
using System.Collections.Generic;
using System.Linq;
using Material.Icons;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 提供来源图标选项与兜底映射逻辑。
/// </summary>
public class IconCatalogService
{
    private static readonly SourceIconOption[] SourceIconPreset =
    {
        new() { Key = nameof(MaterialIconKind.Folder), Label = "默认文件夹", Kind = MaterialIconKind.Folder },
        new() { Key = nameof(MaterialIconKind.FolderOutline), Label = "文件夹(线框)", Kind = MaterialIconKind.FolderOutline },
        new() { Key = nameof(MaterialIconKind.FolderCog), Label = "文件夹设置", Kind = MaterialIconKind.FolderCog },
        new() { Key = nameof(MaterialIconKind.GamepadVariant), Label = "游戏", Kind = MaterialIconKind.GamepadVariant },
        new() { Key = nameof(MaterialIconKind.HammerWrench), Label = "开发", Kind = MaterialIconKind.HammerWrench },
        new() { Key = nameof(MaterialIconKind.RocketLaunch), Label = "发布", Kind = MaterialIconKind.RocketLaunch },
        new() { Key = nameof(MaterialIconKind.Database), Label = "数据", Kind = MaterialIconKind.Database },
        new() { Key = nameof(MaterialIconKind.Cloud), Label = "云端", Kind = MaterialIconKind.Cloud }
    };

    public IReadOnlyList<SourceIconOption> GetSourceIconOptions()
    {
        return SourceIconPreset;
    }

    public MaterialIconKind ResolveSourceIconKind(string? iconKey, string stableSeed)
    {
        if (!string.IsNullOrWhiteSpace(iconKey)
            && Enum.TryParse(iconKey, true, out MaterialIconKind explicitKind))
        {
            return explicitKind;
        }

        if (string.IsNullOrWhiteSpace(stableSeed))
        {
            return MaterialIconKind.Folder;
        }

        var hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(stableSeed));
        var index = hash % SourceIconPreset.Length;
        return SourceIconPreset[index].Kind;
    }

    public string ResolveIconKey(string? iconKey, string stableSeed)
    {
        if (!string.IsNullOrWhiteSpace(iconKey)
            && Enum.TryParse(iconKey, true, out MaterialIconKind explicitKind))
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
