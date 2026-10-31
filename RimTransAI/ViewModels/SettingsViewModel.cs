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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Views;

namespace RimTransAI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly IconCatalogService _iconCatalogService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string _assemblyCSharpPath = string.Empty;
    [ObservableProperty] private int _selectedLanguageIndex;
    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private bool _useCustomPrompt;
    [ObservableProperty] private string _customPrompt = string.Empty;
    [ObservableProperty] private int _selectedTemplateIndex;
    [ObservableProperty] private bool _enableMultiThreadTranslation;
    [ObservableProperty] private int _maxThreads = 4;
    [ObservableProperty] private int _threadIntervalMs = 100;
    [ObservableProperty] private bool _enableAutoBackup = true;
    [ObservableProperty] private string _backupDirectory = string.Empty;
    [ObservableProperty] private int _maxBackupCount = 10;
    [ObservableProperty] private int _backupCompressionLevel = 1;
    [ObservableProperty] private string _validationError = string.Empty;
    [ObservableProperty] private ModSourceFolder? _selectedModSource;
    [ObservableProperty] private TranslationApiProfile? _selectedTranslationApi;

    public ObservableCollection<ModSourceFolder> ModSourceFolders { get; } = new();
    public ObservableCollection<TranslationApiProfile> TranslationApis { get; } = new();

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
            var language = SelectedLanguageIndex == 1 ? "繁體中文" : "简体中文";
            return UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPrompt)
                ? CustomPrompt.Replace("{targetLang}", language)
                : _promptTemplates[0].Replace("{targetLang}", language);
        }
    }

    public SettingsViewModel()
    {
        _configService = new ConfigService();
        _iconCatalogService = new IconCatalogService();
        _logger = NullLogger<SettingsViewModel>.Instance;
        LoadFromService();
    }

    public SettingsViewModel(
        ConfigService configService,
        IconCatalogService iconCatalogService,
        ILogger<SettingsViewModel>? logger = null)
    {
        _configService = configService;
        _iconCatalogService = iconCatalogService;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
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

    partial void OnCustomPromptChanged(string value) => OnPropertyChanged(nameof(PreviewPrompt));

    partial void OnUseCustomPromptChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewPrompt));
        OnPropertyChanged(nameof(UseDefaultPrompt));
    }

    partial void OnSelectedLanguageIndexChanged(int value) => OnPropertyChanged(nameof(PreviewPrompt));

    private void LoadFromService()
    {
        var config = _configService.CurrentConfig;

        AssemblyCSharpPath = config.AssemblyCSharpPath;
        SelectedLanguageIndex = config.TargetLanguage == "ChineseTraditional" ? 1 : 0;
        SelectedThemeIndex = config.AppTheme == "Dark" ? 1 : 0;
        DebugMode = config.DebugMode;
        UseCustomPrompt = config.UseCustomPrompt;
        CustomPrompt = config.CustomPrompt;
        SelectedTemplateIndex = config.PromptTemplateName switch
        {
            "SciFi" => 1,
            "Fantasy" => 2,
            "Formal" => 3,
            _ => 0
        };

        EnableMultiThreadTranslation = config.EnableMultiThreadTranslation;
        MaxThreads = Math.Clamp(config.MaxThreads, 1, 10);
        ThreadIntervalMs = Math.Clamp(config.ThreadIntervalMs, 0, 1000);
        EnableAutoBackup = config.EnableAutoBackup;
        BackupDirectory = config.BackupDirectory ?? string.Empty;
        MaxBackupCount = Math.Max(1, config.MaxBackupCount);
        BackupCompressionLevel = Math.Clamp(config.BackupCompressionLevel, 0, 2);

        TranslationApis.Clear();
        foreach (var api in config.TranslationApis ?? [])
        {
            TranslationApis.Add(api.Clone());
        }

        SelectedTranslationApi = TranslationApis.FirstOrDefault();

        ModSourceFolders.Clear();
        foreach (var source in config.ModSourceFolders ?? [])
        {
            var clone = source.Clone();
            clone.IconKey = _iconCatalogService.ResolveIconKey(clone.IconKey, clone.Id);
            ModSourceFolders.Add(clone);
        }

        SelectedModSource = ModSourceFolders.FirstOrDefault();

        // 核心程序集路径自动探测：1.7 起在游戏根目录，旧版本在 *_Data\Managed。
        // 配置为空或已失效（如游戏升级换了目录）时，从 Mod 源目录向上回溯，并探测 Steam/GOG 常见安装位置。
        if (!RimWorldAssemblyLocator.IsValidAssembly(AssemblyCSharpPath))
        {
            var detected = RimWorldAssemblyLocator.Resolve(
                null,
                ModSourceFolders.Select(source => (string?)source.FolderPath).ToArray());
            if (detected != null)
            {
                AssemblyCSharpPath = detected;
            }
        }
    }

    [RelayCommand]
    private async Task AddTranslationApi()
    {
        await OpenTranslationApiEditorAsync(null);
    }

    [RelayCommand]
    private async Task EditSelectedTranslationApi()
    {
        if (SelectedTranslationApi != null)
        {
            await OpenTranslationApiEditorAsync(SelectedTranslationApi);
        }
    }

    [RelayCommand]
    private void DeleteSelectedTranslationApi()
    {
        if (SelectedTranslationApi == null)
        {
            return;
        }

        var index = TranslationApis.IndexOf(SelectedTranslationApi);
        TranslationApis.Remove(SelectedTranslationApi);
        SelectedTranslationApi = TranslationApis.ElementAtOrDefault(Math.Min(index, TranslationApis.Count - 1));
    }

    [RelayCommand]
    private void MoveSelectedTranslationApiUp() => MoveSelectedTranslationApi(-1);

    [RelayCommand]
    private void MoveSelectedTranslationApiDown() => MoveSelectedTranslationApi(1);

    private void MoveSelectedTranslationApi(int offset)
    {
        if (SelectedTranslationApi == null)
        {
            return;
        }

        var currentIndex = TranslationApis.IndexOf(SelectedTranslationApi);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= TranslationApis.Count)
        {
            return;
        }

        TranslationApis.Move(currentIndex, targetIndex);
    }

    private async Task OpenTranslationApiEditorAsync(TranslationApiProfile? api)
    {
        if (CurrentWindow == null)
        {
            return;
        }

        var editorWindow = new TranslationApiProfileEditorWindow
        {
            DataContext = new TranslationApiProfileEditorViewModel(api)
        };

        var result = await editorWindow.ShowDialog<TranslationApiProfile?>(CurrentWindow);
        if (result == null)
        {
            return;
        }

        var duplicateName = TranslationApis
            .Where(x => !ReferenceEquals(x, api))
            .Any(x => string.Equals(x.Name.Trim(), result.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
        {
            SetValidationError("API 名称已存在，不能重复。");
            return;
        }

        if (api == null)
        {
            TranslationApis.Add(result);
            SelectedTranslationApi = result;
        }
        else
        {
            var index = TranslationApis.IndexOf(api);
            TranslationApis[index] = result;
            SelectedTranslationApi = result;
        }

        ClearValidationError();
    }

    [RelayCommand]
    private async Task AddModSource() => await OpenModSourceEditorAsync(null);

    [RelayCommand]
    private async Task EditModSource(ModSourceFolder? source)
    {
        if (source != null)
        {
            await OpenModSourceEditorAsync(source);
        }
    }

    [RelayCommand]
    private void DeleteModSource(ModSourceFolder? source)
    {
        if (source == null)
        {
            return;
        }

        ModSourceFolders.Remove(source);
        if (ReferenceEquals(SelectedModSource, source))
        {
            SelectedModSource = ModSourceFolders.FirstOrDefault();
        }
    }

    private async Task OpenModSourceEditorAsync(ModSourceFolder? source)
    {
        if (CurrentWindow == null)
        {
            return;
        }

        var editorWindow = new ModSourceEditorWindow
        {
            DataContext = new ModSourceEditorViewModel(_iconCatalogService, source)
        };

        var result = await editorWindow.ShowDialog<ModSourceFolder?>(CurrentWindow);
        if (result == null)
        {
            return;
        }

        var duplicatePath = ModSourceFolders
            .Where(x => !string.Equals(x.Id, result.Id, StringComparison.OrdinalIgnoreCase))
            .Any(x => string.Equals(x.FolderPath, result.FolderPath, StringComparison.OrdinalIgnoreCase));

        if (duplicatePath)
        {
            SetValidationError("该来源目录已存在，不能重复。");
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

        ClearValidationError();
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        var errors = ValidateSettings();
        if (errors.Count > 0)
        {
            SetValidationError($"请检查配置项：{string.Join("、", errors)}");
            _logger.LogUserWarning("设置验证失败，请检查：{InvalidFields}", errors);
            return;
        }

        ClearValidationError();

        var current = _configService.CurrentConfig;
        var newTheme = SelectedThemeIndex == 1 ? "Dark" : "Light";
        var templateName = SelectedTemplateIndex switch
        {
            0 => "Default",
            1 => "SciFi",
            2 => "Fantasy",
            3 => "Formal",
            _ => "Default"
        };

        var newConfig = new AppConfig
        {
            Version = current.Version,
            TranslationApis = TranslationApis.Select(x => x.Clone()).ToList(),
            TargetLanguage = SelectedLanguageIndex == 1 ? "ChineseTraditional" : "ChineseSimplified",
            AppTheme = newTheme,
            AssemblyCSharpPath = AssemblyCSharpPath,
            DebugMode = DebugMode,
            MaxTokensPerBatch = current.MaxTokensPerBatch,
            MinItemsPerBatch = current.MinItemsPerBatch,
            MaxItemsPerBatch = current.MaxItemsPerBatch,
            CustomPrompt = CustomPrompt,
            UseCustomPrompt = UseCustomPrompt,
            PromptTemplateName = templateName,
            EnableMultiThreadTranslation = EnableMultiThreadTranslation,
            MaxThreads = Math.Clamp(MaxThreads, 1, 10),
            ThreadIntervalMs = Math.Clamp(ThreadIntervalMs, 0, 1000),
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

        if (!_configService.SaveConfig(newConfig))
        {
            SetValidationError("设置保存失败，请检查程序目录写入权限。");
            return;
        }

        App.SetTheme(newTheme);
        LoggingBootstrap.SetApiKeys(newConfig.TranslationApis.Select(x => x.ApiKey));
        LoggingBootstrap.SetDebugMode(DebugMode);
        _logger.LogUserSuccess(
            "设置已保存 Theme={Theme} DebugEnabled={DebugEnabled} MultiThreadEnabled={MultiThreadEnabled} MaxThreads={MaxThreads} ApiCount={ApiCount}",
            newTheme,
            DebugMode,
            EnableMultiThreadTranslation,
            newConfig.MaxThreads,
            newConfig.TranslationApis.Count);
        CurrentWindow?.Close();
    }

    private List<string> ValidateSettings()
    {
        var errors = new List<string>();

        // 允许留空：保存时再尝试自动探测一次（1.7 游戏根目录 / 旧版 *_Data\Managed / Steam / GOG）
        var resolvedAssemblyPath = RimWorldAssemblyLocator.Resolve(
            AssemblyCSharpPath,
            ModSourceFolders.Select(source => (string?)source.FolderPath).ToArray());
        if (resolvedAssemblyPath == null)
        {
            errors.Add("Assembly-CSharp.dll 路径");
        }
        else
        {
            AssemblyCSharpPath = resolvedAssemblyPath;
        }

        if (TranslationApis.Count == 0)
        {
            errors.Add("至少添加一个 API");
        }

        var duplicateApiName = TranslationApis
            .Select(x => x.Name.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

        if (TranslationApis.Any(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            errors.Add("API 名称");
        }

        if (duplicateApiName)
        {
            errors.Add("API 名称(存在重复)");
        }

        var enabledApis = TranslationApis.Where(x => x.IsEnabled).ToList();
        if (enabledApis.Count == 0)
        {
            errors.Add("至少启用一个 API");
        }

        foreach (var api in TranslationApis)
        {
            var displayName = string.IsNullOrWhiteSpace(api.Name) ? "未命名 API" : api.Name.Trim();
            var models = api.Models ?? [];
            var duplicateModel = models
                .Select(x => x.ModelId.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);

            if (models.Any(x => string.IsNullOrWhiteSpace(x.ModelId)))
            {
                errors.Add($"API「{displayName}」的模型 ID");
            }

            if (duplicateModel)
            {
                errors.Add($"API「{displayName}」的模型 ID(存在重复)");
            }

            if (!api.IsEnabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(api.ApiUrl) || string.IsNullOrWhiteSpace(api.ApiKey))
            {
                errors.Add($"启用 API「{displayName}」的连接信息");
            }

            if (!models.Any(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.ModelId)))
            {
                errors.Add($"启用 API「{displayName}」的模型");
            }
        }

        if (ModSourceFolders.Any(x => string.IsNullOrWhiteSpace(x.FolderPath) || !Directory.Exists(x.FolderPath)))
        {
            errors.Add("Mod 来源目录(存在无效路径)");
        }

        var duplicateSourcePath = ModSourceFolders
            .Select(x => x.FolderPath.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

        if (duplicateSourcePath)
        {
            errors.Add("Mod 来源目录(存在重复)");
        }

        return errors;
    }

    private void SetValidationError(string message)
    {
        ValidationError = message;
        OnPropertyChanged(nameof(HasValidationError));
    }

    private void ClearValidationError()
    {
        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close();
    }

    [RelayCommand]
    private async Task SelectAssemblyCSharpFile()
    {
        if (CurrentWindow == null)
        {
            return;
        }

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
        if (CurrentWindow == null)
        {
            return;
        }

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
