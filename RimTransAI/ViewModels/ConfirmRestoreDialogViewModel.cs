using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RimTransAI.ViewModels;

public partial class ConfirmRestoreDialogViewModel : ViewModelBase
{
    [ObservableProperty] private string _modName = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _targetLanguage = string.Empty;
    [ObservableProperty] private string _backupFileName = string.Empty;
    [ObservableProperty] private string _backupDate = string.Empty;
    [ObservableProperty] private string _backupSize = string.Empty;

    public bool IsConfirmed { get; private set; } = false;

    public Window? CurrentWindow { get; set; }

    public ConfirmRestoreDialogViewModel()
    {
        // 设计时构造函数
    }

    public ConfirmRestoreDialogViewModel(string modName, string version, string targetLanguage, string backupFileName, string backupDate, string backupSize)
    {
        ModName = modName;
        Version = version;
        TargetLanguage = targetLanguage;
        BackupFileName = backupFileName;
        BackupDate = backupDate;
        BackupSize = backupSize;
    }

    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;
        CurrentWindow?.Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        CurrentWindow?.Close(false);
    }
}
