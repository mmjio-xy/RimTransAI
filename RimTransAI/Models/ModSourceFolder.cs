using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.Material;

namespace RimTransAI.Models;

/// <summary>
/// Mod 来源目录配置项。
/// </summary>
public partial class ModSourceFolder : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _displayName = string.Empty;
    private string _folderPath = string.Empty;
    private string _iconKey = string.Empty;
    private bool _isEnabled = true;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value);
    }

    /// <summary>
    /// IconPacks Material 图标键名（例如 Folder, RocketLaunch）。
    /// </summary>
    public string IconKey
    {
        get => _iconKey;
        set
        {
            if (SetProperty(ref _iconKey, value))
            {
                OnPropertyChanged(nameof(IconKind));
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    [JsonIgnore]
    public PackIconMaterialKind IconKind
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(IconKey)
                && Enum.TryParse(IconKey, true, out PackIconMaterialKind parsed))
            {
                return parsed;
            }

            return PackIconMaterialKind.Folder;
        }
    }

    public ModSourceFolder Clone()
    {
        return new ModSourceFolder
        {
            Id = Id,
            DisplayName = DisplayName,
            FolderPath = FolderPath,
            IconKey = IconKey,
            IsEnabled = IsEnabled
        };
    }
}
