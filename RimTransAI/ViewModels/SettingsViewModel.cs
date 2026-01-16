using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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

    // Assembly-CSharp.dll 路径
    [ObservableProperty] private string _assemblyCSharpPath = string.Empty;

    // 下拉框选中的索引 (0: 简中, 1: 繁中)
    [ObservableProperty] private int _selectedLanguageIndex = 0;

    [ObservableProperty] private int _selectedThemeIndex = 0;
    [ObservableProperty] private string _targetModel = string.Empty;

    // 调试模式开关
    [ObservableProperty] private bool _debugMode = false;

    // 设计时构造函数
    public SettingsViewModel()
    {
        // 设计时使用默认配置服务，避免空引用异常
        _configService = new ConfigService();
        LoadFromService();
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
        AssemblyCSharpPath = cfg.AssemblyCSharpPath;

        // 简单映射语言选择
        SelectedLanguageIndex = cfg.TargetLanguage == "ChineseTraditional" ? 1 : 0;
        // === 加载主题设置 ===
        // 如果配置是 Dark，索引设为 1，否则设为 0
        SelectedThemeIndex = cfg.AppTheme == "Dark" ? 1 : 0;

        // 加载调试模式设置
        DebugMode = cfg.DebugMode;
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
            AppTheme = newTheme,
            AssemblyCSharpPath = AssemblyCSharpPath,
            DebugMode = DebugMode
        };

        // 保存到磁盘
        _configService.SaveConfig(newConfig);

        // 4. === 立即应用主题 ===
        App.SetTheme(newTheme);

        // 5. === 立即应用调试模式 ===
        Logger.SetDebugMode(DebugMode);

        // 关闭窗口
        CurrentWindow?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close();
    }

    /// <summary>
    /// 选择 Assembly-CSharp.dll 文件
    /// </summary>
    [RelayCommand]
    private async Task SelectAssemblyCSharpFile()
    {
        if (CurrentWindow == null) return;

        var files = await CurrentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Assembly-CSharp.dll 文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("DLL 文件")
                {
                    Patterns = new[] { "*.dll" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        if (files.Count > 0)
        {
            AssemblyCSharpPath = files[0].Path.LocalPath;
        }
    }
}