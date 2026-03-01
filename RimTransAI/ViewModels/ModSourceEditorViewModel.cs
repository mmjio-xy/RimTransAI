using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Models;
using RimTransAI.Services;

namespace RimTransAI.ViewModels;

public partial class ModSourceEditorViewModel : ViewModelBase
{
    private readonly IconCatalogService _iconCatalogService;
    private readonly string _sourceId;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private SourceIconOption? _selectedIconOption;
    [ObservableProperty] private string _validationError = string.Empty;

    public ObservableCollection<SourceIconOption> IconOptions { get; } = new();
    public Window? CurrentWindow { get; set; }
    public string DialogTitle { get; }
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public ModSourceEditorViewModel(IconCatalogService iconCatalogService, ModSourceFolder? source)
    {
        _iconCatalogService = iconCatalogService;
        _sourceId = source?.Id ?? Guid.NewGuid().ToString("N");
        DialogTitle = source == null ? "添加 Mod 来源" : "编辑 Mod 来源";

        foreach (var option in _iconCatalogService.GetSourceIconOptions())
        {
            IconOptions.Add(option);
        }

        DisplayName = source?.DisplayName ?? string.Empty;
        FolderPath = source?.FolderPath ?? string.Empty;
        IsEnabled = source?.IsEnabled ?? true;
        SelectedIconOption = _iconCatalogService.GetOptionByKey(source?.IconKey, _sourceId);
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (CurrentWindow == null) return;

        var folders = await CurrentWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 Mod 来源目录",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        FolderPath = path;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(FolderPath))
        {
            ValidationError = "请选择来源目录。";
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        if (!Directory.Exists(FolderPath))
        {
            ValidationError = "来源目录不存在，请重新选择。";
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        var finalName = DisplayName;
        if (string.IsNullOrWhiteSpace(finalName))
        {
            finalName = Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var finalIconKey = SelectedIconOption?.Key
                           ?? _iconCatalogService.ResolveIconKey(null, _sourceId);

        var result = new ModSourceFolder
        {
            Id = _sourceId,
            DisplayName = finalName,
            FolderPath = FolderPath,
            IconKey = finalIconKey,
            IsEnabled = IsEnabled
        };

        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));
        CurrentWindow?.Close(result);
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close(null);
    }
}
