using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Models;
using RimTransAI.Services;

// 用于关闭窗口

namespace RimTransAI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    [ObservableProperty] private string _apiKey = string.Empty;

    // 绑定到 UI 的属性
    [ObservableProperty] private string _apiUrl = string.Empty;

    // 下拉框选中的索引 (0: 简中, 1: 繁中)
    [ObservableProperty] private int _selectedLanguageIndex = 0;

    [ObservableProperty] private int _selectedThemeIndex = 0;
    [ObservableProperty] private string _targetModel = string.Empty;

    // 设计时构造函数
    public SettingsViewModel()
    {
    }

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        LoadFromService();
    }

    // 这一行是为了能让 View 代码隐藏文件传 Window 进来关闭自己
    public Window? CurrentWindow { get; set; }

    private void LoadFromService()
    {
        var cfg = _configService.CurrentConfig;
        ApiUrl = cfg.ApiUrl;
        ApiKey = cfg.ApiKey;
        TargetModel = cfg.TargetModel;

        // 简单映射语言选择
        SelectedLanguageIndex = cfg.TargetLanguage == "ChineseTraditional" ? 1 : 0;
        // === 加载主题设置 ===
        // 如果配置是 Dark，索引设为 1，否则设为 0
        SelectedThemeIndex = cfg.AppTheme == "Dark" ? 1 : 0;
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        // 1. 获取当前选中的主题字符串
        string newTheme = SelectedThemeIndex == 1 ? "Dark" : "Light";
        // 构建新配置对象
        var newConfig = new AppConfig
        {
            ApiUrl = ApiUrl,
            ApiKey = ApiKey,
            TargetModel = TargetModel,
            TargetLanguage = SelectedLanguageIndex == 1 ? "ChineseTraditional" : "ChineseSimplified",
            AppTheme = newTheme
        };

        // 保存到磁盘
        _configService.SaveConfig(newConfig);
        
        // 4. === 立即应用主题 ===
        App.SetTheme(newTheme);

        // 关闭窗口
        CurrentWindow?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close();
    }
}