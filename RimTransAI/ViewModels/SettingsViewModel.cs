using System;
using System.Collections.Generic;
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
using RimTransAI.Views;

namespace RimTransAI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly IconCatalogService _iconCatalogService;

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _apiUrl = string.Empty;
    [ObservableProperty] private string _assemblyCSharpPath = string.Empty;
    [ObservableProperty] private int _selectedLanguageIndex;
    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private string _targetModel = string.Empty;
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private bool _useCustomPrompt;
    [ObservableProperty] private string _customPrompt = string.Empty;
    [ObservableProperty] private int _selectedTemplateIndex;
    [ObservableProperty] private bool _enableMultiThreadTranslation;
    [ObservableProperty] private int _maxThreads = 4;
    [ObservableProperty] private int _threadIntervalMs = 100;
    [ObservableProperty] private bool _enableAutoBackup = true;
    [ObservableProperty] private string _backupDirectory = "";
    [ObservableProperty] private int _maxBackupCount = 10;
    [ObservableProperty] private int _backupCompressionLevel = 1;
    [ObservableProperty] private string _validationError = string.Empty;

    [ObservableProperty] private ModSourceFolder? _selectedModSource;

    public ObservableCollection<ModSourceFolder> ModSourceFolders { get; } = new();

    public bool UseDefaultPrompt => !UseCustomPrompt;
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    private readonly Dictionary<int, string> _promptTemplates = new()
    {
        { 0, "You are a professional translator for RimWorld. Target: {targetLang}. Rules: Preserve XML tags, variables like {{0}}, and paths. Input/Output is JSON." },
        { 1, "You are a professional RimWorld translator specializing in sci-fi content. Target: {targetLang}. Maintain futuristic and technical terminology. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." },
        { 2, "You are a professional RimWorld translator specializing in fantasy content. Target: {targetLang}. Use mystical and archaic language where appropriate. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." },
        { 3, "You are a professional RimWorld translator. Target: {targetLang}. Use formal, authoritative language. Rules: Preserve XML tags, variables, paths. Input/Output is JSON." }
    };

    public string PreviewPrompt
    {
        get
        {
            string langDisplay = SelectedLanguageIndex == 1 ? "繁體中文" : "简体中文";
            if (UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPrompt))
            {
                return CustomPrompt.Replace("{targetLang}", langDisplay);
            }

            return _promptTemplates[0].Replace("{targetLang}", langDisplay);
        }
    }

    public SettingsViewModel()
    {
        _configService = new ConfigService();
        _iconCatalogService = new IconCatalogService();
        LoadFromService();
    }

    public SettingsViewModel(ConfigService configService, IconCatalogService iconCatalogService)
    {
        _configService = configService;
        _iconCatalogService = iconCatalogService;
        LoadFromService();
    }

    public Window? CurrentWindow { get; set; }

    partial void OnSelectedTemplateIndexChanged(int value)
    {
        if (value >= 0 && value < _promptTemplates.Count)
        {
            CustomPrompt = _promptTemplates[value];
            UseCustomPrompt = true;
        }
    }

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
        SelectedLanguageIndex = cfg.TargetLanguage == "ChineseTraditional" ? 1 : 0;
        SelectedThemeIndex = cfg.AppTheme == "Dark" ? 1 : 0;
        DebugMode = cfg.DebugMode;
        UseCustomPrompt = cfg.UseCustomPrompt;
        CustomPrompt = cfg.CustomPrompt;

        SelectedTemplateIndex = cfg.PromptTemplateName switch
        {
            "SciFi" => 1,
            "Fantasy" => 2,
            "Formal" => 3,
            _ => 0
        };

        EnableMultiThreadTranslation = cfg.EnableMultiThreadTranslation;
        MaxThreads = Math.Clamp(cfg.MaxThreads, 1, 10);
        ThreadIntervalMs = Math.Max(0, cfg.ThreadIntervalMs);

        EnableAutoBackup = cfg.EnableAutoBackup;
        BackupDirectory = cfg.BackupDirectory ?? "";
        MaxBackupCount = Math.Max(1, cfg.MaxBackupCount);
        BackupCompressionLevel = Math.Clamp(cfg.BackupCompressionLevel, 0, 2);

        ModSourceFolders.Clear();
        foreach (var source in cfg.ModSourceFolders ?? new List<ModSourceFolder>())
        {
            var clone = source.Clone();
            clone.IconKey = _iconCatalogService.ResolveIconKey(clone.IconKey, clone.Id);
            ModSourceFolders.Add(clone);
        }

        SelectedModSource = ModSourceFolders.FirstOrDefault();
    }

    [RelayCommand]
    private async Task AddModSource()
    {
        await OpenModSourceEditorAsync(null);
    }

    [RelayCommand]
    private async Task EditModSource(ModSourceFolder? source)
    {
        if (source == null) return;
        await OpenModSourceEditorAsync(source);
    }

    [RelayCommand]
    private void DeleteModSource(ModSourceFolder? source)
    {
        if (source == null) return;

        ModSourceFolders.Remove(source);
        if (ReferenceEquals(SelectedModSource, source))
        {
            SelectedModSource = ModSourceFolders.FirstOrDefault();
        }
    }

    private async Task OpenModSourceEditorAsync(ModSourceFolder? source)
    {
        if (CurrentWindow == null) return;

        var editorVm = new ModSourceEditorViewModel(_iconCatalogService, source);
        var editorWindow = new ModSourceEditorWindow
        {
            DataContext = editorVm
        };

        var result = await editorWindow.ShowDialog<ModSourceFolder?>(CurrentWindow);
        if (result == null) return;

        var duplicatePath = ModSourceFolders
            .Where(x => !string.Equals(x.Id, result.Id, StringComparison.OrdinalIgnoreCase))
            .Any(x => string.Equals(x.FolderPath, result.FolderPath, StringComparison.OrdinalIgnoreCase));

        if (duplicatePath)
        {
            ValidationError = "该来源目录已存在，不能重复。";
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        if (source == null)
        {
            ModSourceFolders.Add(result);
            SelectedModSource = result;
        }
        else
        {
            source.DisplayName = result.DisplayName;
            source.FolderPath = result.FolderPath;
            source.IconKey = result.IconKey;
            source.IsEnabled = result.IsEnabled;
            SelectedModSource = source;
        }

        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(AssemblyCSharpPath))
            errors.Add("Assembly-CSharp.dll 路径");

        if (string.IsNullOrWhiteSpace(ApiUrl))
            errors.Add("API 地址");

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("API Key");

        if (string.IsNullOrWhiteSpace(TargetModel))
            errors.Add("模型名称");

        if (ModSourceFolders.Any(x => string.IsNullOrWhiteSpace(x.FolderPath) || !Directory.Exists(x.FolderPath)))
            errors.Add("Mod 来源目录(存在无效路径)");

        var duplicateSourcePath = ModSourceFolders
            .Select(x => x.FolderPath.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x)
            .Any(g => g.Count() > 1);

        if (duplicateSourcePath)
            errors.Add("Mod 来源目录(存在重复)");

        if (errors.Count > 0)
        {
            ValidationError = $"请检查配置项：{string.Join("、", errors)}";
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));

        string newTheme = SelectedThemeIndex == 1 ? "Dark" : "Light";
        string templateName = SelectedTemplateIndex switch
        {
            0 => "Default",
            1 => "SciFi",
            2 => "Fantasy",
            3 => "Formal",
            _ => "Default"
        };

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
            EnableAutoBackup = EnableAutoBackup,
            BackupDirectory = BackupDirectory,
            MaxBackupCount = Math.Max(1, MaxBackupCount),
            BackupCompressionLevel = Math.Clamp(BackupCompressionLevel, 0, 2),
            ModSourceFolders = ModSourceFolders
                .Select(x =>
                {
                    var clone = x.Clone();
                    clone.IconKey = _iconCatalogService.ResolveIconKey(clone.IconKey, clone.Id);
                    return clone;
                })
                .ToList()
        };

        _configService.SaveConfig(newConfig);
        App.SetTheme(newTheme);
        Logger.SetDebugMode(DebugMode);
        CurrentWindow?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close();
    }

    [RelayCommand]
    private async Task SelectAssemblyCSharpFile()
    {
        if (CurrentWindow == null) return;

        var files = await CurrentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Assembly-CSharp.dll 文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DLL 文件") { Patterns = ["*.dll"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            AssemblyCSharpPath = files[0].Path.LocalPath;
        }
    }

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
