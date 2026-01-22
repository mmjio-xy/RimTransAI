using System;
using System.Collections.Generic;
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

    // 提示词设置
    [ObservableProperty] private bool _useCustomPrompt = false;
    [ObservableProperty] private string _customPrompt = string.Empty;
    [ObservableProperty] private int _selectedTemplateIndex = 0;

    // 是否使用默认提示词（用于避免 XAML 负向绑定）
    public bool UseDefaultPrompt => !UseCustomPrompt;

    // 多线程翻译设置
    [ObservableProperty] private bool _enableMultiThreadTranslation = false;
    [ObservableProperty] private int _maxThreads = 4;
    [ObservableProperty] private int _threadIntervalMs = 100;

    // ========== 备份配置 ==========
    [ObservableProperty] private bool _enableAutoBackup = true;
    [ObservableProperty] private string _backupDirectory = "";
    [ObservableProperty] private int _maxBackupCount = 10;
    [ObservableProperty] private int _backupCompressionLevel = 1;  // 0: Fastest, 1: Optimal, 2: SmallestSize

    // 验证错误信息
    [ObservableProperty] private string _validationError = string.Empty;

    // 是否有验证错误
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    // 预设模板
    private readonly Dictionary<int, string> _promptTemplates = new()
    {
        { 0, "You are a professional translator for RimWorld. Target: {targetLang}. Rules: Preserve XML tags, variables like {{0}}, and paths. Input/Output is JSON." },
        { 1, "You are a professional RimWorld translator specializing in sci-fi content. Target: {targetLang}. Maintain futuristic and technical terminology. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." },
        { 2, "You are a professional RimWorld translator specializing in fantasy content. Target: {targetLang}. Use mystical and archaic language where appropriate. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." },
        { 3, "You are a professional RimWorld translator. Target: {targetLang}. Use formal, authoritative language. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." }
    };

    // 预览属性（显示最终发送给 LLM 的提示词）
    public string PreviewPrompt
    {
        get
        {
            string langDisplay = SelectedLanguageIndex == 1 ? "繁體中文" : "简体中文";

            if (UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPrompt))
            {
                return CustomPrompt.Replace("{targetLang}", langDisplay);
            }
            else
            {
                return _promptTemplates[0].Replace("{targetLang}", langDisplay);
            }
        }
    }

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

    // 模板选择变化处理
    partial void OnSelectedTemplateIndexChanged(int value)
    {
        if (value >= 0 && value < _promptTemplates.Count)
        {
            CustomPrompt = _promptTemplates[value];
            UseCustomPrompt = true;
        }
    }

    // 提示词相关属性变化时更新预览
    partial void OnCustomPromptChanged(string value)
    {
        OnPropertyChanged(nameof(PreviewPrompt));
    }

    partial void OnUseCustomPromptChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewPrompt));
        OnPropertyChanged(nameof(UseDefaultPrompt));
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewPrompt));
    }

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

        // 加载提示词配置
        UseCustomPrompt = cfg.UseCustomPrompt;
        CustomPrompt = cfg.CustomPrompt;

        // 根据模板名称设置下拉框选择
        SelectedTemplateIndex = cfg.PromptTemplateName switch
        {
            "SciFi" => 1,
            "Fantasy" => 2,
            "Formal" => 3,
            _ => 0
        };

        // 加载多线程配置
        EnableMultiThreadTranslation = cfg.EnableMultiThreadTranslation;
        MaxThreads = Math.Clamp(cfg.MaxThreads, 1, 10);
        ThreadIntervalMs = Math.Max(0, cfg.ThreadIntervalMs);

        // 加载备份配置
        EnableAutoBackup = cfg.EnableAutoBackup;
        BackupDirectory = cfg.BackupDirectory ?? "";
        MaxBackupCount = Math.Max(1, cfg.MaxBackupCount);
        BackupCompressionLevel = Math.Clamp(cfg.BackupCompressionLevel, 0, 2);
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        // 验证必填项
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(AssemblyCSharpPath))
            errors.Add("Assembly-CSharp.dll 路径");

        if (string.IsNullOrWhiteSpace(ApiUrl))
            errors.Add("API 地址");

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("API Key");

        if (string.IsNullOrWhiteSpace(TargetModel))
            errors.Add("模型名称");

        if (errors.Count > 0)
        {
            ValidationError = $"请填写必填项：{string.Join("、", errors)}";
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        // 清除验证错误
        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));

        // 1. 获取当前选中的主题字符串
        string newTheme = SelectedThemeIndex == 1 ? "Dark" : "Light";
        // 获取模板名称
        string templateName = SelectedTemplateIndex switch
        {
            0 => "Default",
            1 => "SciFi",
            2 => "Fantasy",
            3 => "Formal",
            _ => "Default"
        };

        // 构建新配置对象
        var newConfig = new AppConfig
        {
            ApiUrl = ApiUrl,
            ApiKey = ApiKey,
            TargetModel = TargetModel,
            TargetLanguage = SelectedLanguageIndex == 1 ? "ChineseTraditional" : "ChineseSimplified",
            AppTheme = newTheme,
            AssemblyCSharpPath = AssemblyCSharpPath,
            DebugMode = DebugMode,
            CustomPrompt = CustomPrompt,
            UseCustomPrompt = UseCustomPrompt,
            PromptTemplateName = templateName,
            EnableMultiThreadTranslation = EnableMultiThreadTranslation,
            MaxThreads = Math.Clamp(MaxThreads, 1, 10),
            ThreadIntervalMs = Math.Max(0, ThreadIntervalMs),
            // 备份配置
            EnableAutoBackup = EnableAutoBackup,
            BackupDirectory = BackupDirectory,
            MaxBackupCount = Math.Max(1, MaxBackupCount),
            BackupCompressionLevel = Math.Clamp(BackupCompressionLevel, 0, 2)
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

    /// <summary>
    /// 选择备份目录
    /// </summary>
    [RelayCommand]
    private async Task SelectBackupDirectory()
    {
        if (CurrentWindow == null) return;

        var folders = await CurrentWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择备份存储目录",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            BackupDirectory = folders[0].Path.LocalPath;
        }
    }
}