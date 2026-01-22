using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Services;
using RimTransAI.Views;

namespace RimTransAI.ViewModels;

public partial class BackupManagerViewModel : ViewModelBase
{
    private readonly BackupService _backupService;

    [ObservableProperty] private ObservableCollection<BackupInfoViewModel> _backups = new();
    [ObservableProperty] private string _currentPackageId = "";
    [ObservableProperty] private bool _isOnlyCurrentMod = true;

    // ========== 恢复备份需要的上下文信息 ==========
    [ObservableProperty] private string _currentModPath = "";
    [ObservableProperty] private string _targetLanguage = "";

    // ========== 搜索和排序相关属性 ==========
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _searchTypeIndex = 0;  // 0: Mod名称, 1: PackageId
    [ObservableProperty] private bool _isSortDescending = true;

    public string SortButtonText => IsSortDescending ? "最新优先" : "最早优先";

    public Window? CurrentWindow { get; set; }

    public BackupManagerViewModel()
    {
        // 设计时构造函数
        _backupService = new BackupService(new ConfigService());
    }

    public BackupManagerViewModel(BackupService backupService, string? currentPackageId = null)
    {
        _backupService = backupService;
        CurrentPackageId = currentPackageId ?? "";
        LoadBackups();
    }

    [RelayCommand]
    public void Refresh()
    {
        LoadBackups();
    }

    [RelayCommand]
    public void OpenBackupDirectory()
    {
        string backupDir = _backupService.GetBackupDirectory();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = backupDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开备份目录失败: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void DeleteSelectedBackup(BackupInfoViewModel? backup)
    {
        if (backup == null) return;

        bool success = _backupService.DeleteBackup(backup.FilePath);
        if (success)
        {
            Backups.Remove(backup);
        }
    }

    [RelayCommand]
    public async Task RestoreBackup(BackupInfoViewModel? backup)
    {
        if (backup == null || CurrentWindow == null)
        {
            return;
        }

        // 检查备份文件是否存在
        if (!backup.FileExists)
        {
            await ShowMessageDialog("无法恢复", "备份文件不存在，可能已被删除或移动。");
            return;
        }

        // 检查是否有当前 Mod 路径
        if (string.IsNullOrEmpty(CurrentModPath))
        {
            Logger.Warning("恢复备份失败：未选择 Mod 文件夹");
            await ShowMessageDialog("无法恢复", "请先在主界面选择要恢复到的 Mod 文件夹");
            return;
        }

        // 获取备份文件信息
        var backupFileName = Path.GetFileName(backup.FilePath);
        var backupDate = backup.CreationTimeDisplay;
        var backupSize = backup.FileSizeDisplay;

        // 获取 Mod 名称
        string modName = backup.ModName ?? CurrentPackageId.Replace(".", "_");
        string versionDisplay = backup.VersionDisplay ?? backup.Version ?? "Unknown";
        string targetLanguage = TargetLanguage ?? "Unknown";

        // 显示确认对话框
        var dialog = new Views.ConfirmRestoreDialog(modName, versionDisplay, targetLanguage, backupFileName, backupDate,
            backupSize);
        var confirmResult = await dialog.ShowDialog<bool>(CurrentWindow);

        if (confirmResult == true)
        {
            // 用户确认恢复
            string version = backup.Version ?? "";
            var restoreResult = await Task.Run(() =>
            {
                return _backupService.RestoreFromBackup(
                    CurrentModPath,
                    backup.PackageId,
                    version);
            });

            if (restoreResult.Success)
            {
                // 显示成功提示
                await ShowMessageDialog("恢复成功", $"备份已成功恢复到：\n{restoreResult.RestoredPath}");
                // 关闭窗口
                CurrentWindow?.Close();
            }
            else
            {
                // 显示失败原因
                await ShowMessageDialog("恢复失败", restoreResult.ErrorMessage);
            }
        }
    }

    /// <summary>
    /// 显示消息对话框
    /// </summary>
    private async Task ShowMessageDialog(string title, string message)
    {
        if (CurrentWindow == null) return;

        var msgBox = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Avalonia.Controls.Button
                    {
                        Content = "确定",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    }
                }
            }
        };
        var btn = ((Avalonia.Controls.StackPanel)msgBox.Content).Children[1] as Avalonia.Controls.Button;
        btn!.Click += (_, _) => msgBox.Close();
        await msgBox.ShowDialog(CurrentWindow);
    }

    [RelayCommand]
    public void Close()
    {
        CurrentWindow?.Close();
    }

    partial void OnIsOnlyCurrentModChanged(bool value)
    {
        LoadBackups();
    }

    partial void OnSearchTextChanged(string value)
    {
        LoadBackups();
    }

    partial void OnSearchTypeIndexChanged(int value)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            LoadBackups();
        }
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortButtonText));
        LoadBackups();
    }

    [RelayCommand]
    public void ToggleSortDirection()
    {
        IsSortDescending = !IsSortDescending;
    }

    [RelayCommand]
    public void SortAscending()
    {
        IsSortDescending = false;
    }

    [RelayCommand]
    public void SortDescending()
    {
        IsSortDescending = true;
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = "";
    }

    private void LoadBackups()
    {
        Backups.Clear();

        // 1. 获取原始数据（保留现有 PackageId 过滤）
        string? packageIdFilter = IsOnlyCurrentMod && !string.IsNullOrEmpty(CurrentPackageId)
            ? CurrentPackageId
            : null;

        var backups = _backupService.GetAllBackups(packageIdFilter);

        // 2. 应用搜索过滤
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.Trim().ToLowerInvariant();
            backups = SearchTypeIndex switch
            {
                0 => backups.Where(b => b.ModName.ToLowerInvariant().Contains(searchLower)).ToList(),
                1 => backups.Where(b => b.PackageId.ToLowerInvariant().Contains(searchLower)).ToList(),
                _ => backups
            };
        }

        // 3. 应用排序
        backups = IsSortDescending
            ? backups.OrderByDescending(b => b.CreationTime).ToList()
            : backups.OrderBy(b => b.CreationTime).ToList();

        // 4. 填充集合
        foreach (var backup in backups)
        {
            Backups.Add(new BackupInfoViewModel(backup));
        }
    }
}

public partial class BackupInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _modName = string.Empty;
    [ObservableProperty] private string _packageId = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _versionDisplay = string.Empty;
    [ObservableProperty] private string _timestampStr = string.Empty;
    [ObservableProperty] private DateTime _creationTime;
    [ObservableProperty] private string _creationTimeDisplay = string.Empty;
    [ObservableProperty] private long _fileSizeBytes;
    [ObservableProperty] private string _fileSizeDisplay = string.Empty;
    [ObservableProperty] private bool _fileExists = true;

    public BackupInfoViewModel(BackupInfo info)
    {
        FilePath = info.FilePath;
        ModName = info.ModName;
        PackageId = info.PackageId;
        Version = info.Version;
        VersionDisplay = info.VersionDisplay;
        TimestampStr = info.TimestampStr;
        CreationTime = info.CreationTime;
        CreationTimeDisplay = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        FileSizeBytes = info.FileSizeBytes;
        FileSizeDisplay = FormatFileSize(info.FileSizeBytes);
        FileExists = info.FileExists;
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        else if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024} KB";
        }
        else
        {
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}