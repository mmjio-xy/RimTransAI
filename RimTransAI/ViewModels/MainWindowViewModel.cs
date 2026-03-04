using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Views;

namespace RimTransAI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly FileGeneratorService _fileGeneratorService;
    private readonly LlmService _llmService;
    private readonly ModParserService _modParserService;
    private readonly BatchingService _batchingService;
    private readonly ModInfoService _modInfoService;
    private readonly BackupService _backupService;
    private readonly WorkspaceService _workspaceService;

    private readonly Dictionary<string, List<TranslationItem>> _modItemsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkspaceModItem> _allWorkspaceMods = [];

    private List<TranslationItem> _allItems = new();
    private string _currentModPath = string.Empty;
    private string? _currentPackageId;
    private string? _currentModName;
    private CancellationTokenSource? _translationCts;

    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private string _logOutput = "就绪。请先在设置页配置 Mod 来源目录。";
    [ObservableProperty] private string _latestLogLine = "就绪。请先在设置页配置 Mod 来源目录。";
    [ObservableProperty] private string _selectedVersion = "全部";
    [ObservableProperty] private ModInfoViewModel? _modInfoViewModel;
    [ObservableProperty] private WorkspaceModItem? _selectedMod;
    [ObservableProperty] private string _modSearchText = string.Empty;
    [ObservableProperty] private int _modSearchModeIndex;
    [ObservableProperty] private string _currentDataState = "请选择中栏 Mod，并点击“加载翻译条目”。";
    [ObservableProperty] private bool _isTranslationTableReadOnly = true;
    [ObservableProperty] private bool _isCurrentModLoaded;

    public ObservableCollection<TranslationItem> TranslationItems { get; } = new();
    public ObservableCollection<string> AvailableVersions { get; } = new();
    public ObservableCollection<WorkspaceModItem> WorkspaceMods { get; } = new();
    public string AppDisplayTitle { get; } = BuildAppDisplayTitle();

    private static string BuildAppDisplayTitle()
    {
        var version = typeof(global::RimTransAI.App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return $"RimTrans AI v{version}";
    }

    partial void OnLogOutputChanged(string value)
    {
        var lines = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        LatestLogLine = lines.Length > 0 ? lines[^1] : string.Empty;
    }

    public MainWindowViewModel()
    {
        var reflectionAnalyzer = new ReflectionAnalyzer();
        _configService = new ConfigService();
        _modParserService = new ModParserService(reflectionAnalyzer, _configService);
        _llmService = new LlmService();
        _fileGeneratorService = new FileGeneratorService();
        _batchingService = new BatchingService();
        _modInfoService = new ModInfoService();
        _backupService = new BackupService(_configService);
        _workspaceService = new WorkspaceService(_modInfoService, new IconCatalogService());

        InitializeCollections();
        LoadWorkspaceFromConfig();
    }

    public MainWindowViewModel(
        ModParserService modParserService,
        LlmService llmService,
        FileGeneratorService fileGeneratorService,
        ConfigService configService,
        BatchingService batchingService,
        ModInfoService modInfoService,
        BackupService backupService,
        WorkspaceService workspaceService)
    {
        _modParserService = modParserService;
        _llmService = llmService;
        _fileGeneratorService = fileGeneratorService;
        _configService = configService;
        _batchingService = batchingService;
        _modInfoService = modInfoService;
        _backupService = backupService;
        _workspaceService = workspaceService;

        InitializeCollections();
        LoadWorkspaceFromConfig();
    }

    private void InitializeCollections()
    {
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");
        SelectedVersion = "全部";
        ModSearchModeIndex = 0;
    }

    partial void OnSelectedVersionChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnModSearchTextChanged(string value)
    {
        ApplyWorkspaceFilter();
    }

    partial void OnModSearchModeIndexChanged(int value)
    {
        ApplyWorkspaceFilter();
    }

    partial void OnSelectedModChanged(WorkspaceModItem? value)
    {
        OnPropertyChanged(nameof(CurrentModDisplayName));
        NotifyLoadActionStateChanged();

        if (value == null)
        {
            ShowEmptyDataState("请选择中栏 Mod，并点击“加载翻译条目”。");
            ModInfoViewModel = null;
            _currentModPath = string.Empty;
            _currentPackageId = null;
            _currentModName = null;
            return;
        }

        _currentModPath = value.ModPath;
        LoadModInfo(value.ModPath);

        if (_modItemsCache.TryGetValue(value.ModPath, out var cachedItems))
        {
            BindItems(cachedItems, editable: false, snapshotOnly: true);
            CurrentDataState = "缓存快照（只读）。点击“刷新翻译条目”重新扫描并进入可编辑状态。";
        }
        else
        {
            ShowEmptyDataState("当前 Mod 尚未加载翻译条目，请点击“加载翻译条目”。");
        }
    }

    public string CurrentModDisplayName => SelectedMod?.Name ?? "未选择";
    public bool HasSelectedModCache => SelectedMod != null && _modItemsCache.ContainsKey(SelectedMod.ModPath);
    public string LoadOrRefreshTranslationText => HasSelectedModCache ? "刷新翻译条目" : "加载翻译条目";

    private void NotifyLoadActionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedModCache));
        OnPropertyChanged(nameof(LoadOrRefreshTranslationText));
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        var app = (App)Application.Current!;
        if (app.Services == null) return;

        var settingsVm = app.Services.GetRequiredService<SettingsViewModel>();

        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };

        await settingsWindow.ShowDialog(topLevel);
        LoadWorkspaceFromConfig();
        LogOutput += "\n设置已更新，来源列表已刷新。";
    }

    [RelayCommand]
    private async Task OpenBackupManager()
    {
        if (SelectedMod == null)
        {
            LogOutput = "请先在中栏选择一个 Mod。";
            return;
        }

        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        var app = (App)Application.Current!;
        if (app.Services == null) return;

        var backupManagerVm = app.Services.GetRequiredService<BackupManagerViewModel>();
        backupManagerVm.CurrentModPath = SelectedMod.ModPath;
        backupManagerVm.CurrentPackageId = _currentPackageId ?? "";
        backupManagerVm.TargetLanguage = _configService.CurrentConfig.TargetLanguage;
        backupManagerVm.Refresh();

        var backupManagerWindow = new BackupManagerWindow
        {
            DataContext = backupManagerVm
        };

        backupManagerVm.CurrentWindow = backupManagerWindow;
        await backupManagerWindow.ShowDialog(topLevel);
    }

    [RelayCommand]
    private void RefreshWorkspace()
    {
        LoadWorkspaceFromConfig();
    }

    private void LoadWorkspaceFromConfig()
    {
        var previousPath = SelectedMod?.ModPath;

        _allWorkspaceMods.Clear();
        var configSources = _configService.CurrentConfig.ModSourceFolders ?? new List<ModSourceFolder>();
        var discoveredMods = _workspaceService.DiscoverModsFromSources(configSources);

        foreach (var mod in discoveredMods)
        {
            if (_modItemsCache.TryGetValue(mod.ModPath, out var cached))
            {
                mod.ItemCount = cached.Count;
                mod.Status = "已缓存";
            }

            _allWorkspaceMods.Add(mod);
        }

        if (_allWorkspaceMods.Count == 0)
        {
            WorkspaceMods.Clear();
            SelectedMod = null;
            LogOutput = "未发现 Mod。请在设置页添加有效的来源目录。";
            return;
        }

        ApplyWorkspaceFilter(previousPath);

        LogOutput = $"已发现 {_allWorkspaceMods.Count} 个 Mod，可在中栏选择后手动加载翻译条目。";
    }

    private void ApplyWorkspaceFilter(string? preferredPath = null)
    {
        var keyword = ModSearchText.Trim();
        IEnumerable<WorkspaceModItem> filtered = _allWorkspaceMods;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = ModSearchModeIndex == 1
                ? filtered.Where(x => (x.PackageId ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                : filtered.Where(x => (x.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        WorkspaceMods.Clear();
        foreach (var mod in filteredList)
        {
            WorkspaceMods.Add(mod);
        }

        if (WorkspaceMods.Count == 0)
        {
            return;
        }

        var targetPath = preferredPath ?? SelectedMod?.ModPath;
        var target = !string.IsNullOrWhiteSpace(targetPath)
            ? WorkspaceMods.FirstOrDefault(x => string.Equals(x.ModPath, targetPath, StringComparison.OrdinalIgnoreCase))
            : null;

        if (target != null)
        {
            SelectedMod = target;
            return;
        }

        if (SelectedMod == null || WorkspaceMods.All(x => !string.Equals(x.ModPath, SelectedMod.ModPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedMod = WorkspaceMods[0];
        }
    }

    [RelayCommand]
    private async Task LoadOrRefreshTranslationItems()
    {
        await LoadSelectedModItemsAsync(forceRescan: HasSelectedModCache);
    }

    private async Task LoadSelectedModItemsAsync(bool forceRescan)
    {
        if (SelectedMod == null)
        {
            LogOutput = "请先选择一个 Mod。";
            return;
        }

        var config = _configService.CurrentConfig;
        if (string.IsNullOrWhiteSpace(config.AssemblyCSharpPath))
        {
            LogOutput = "错误：未配置 Assembly-CSharp.dll 路径，请先前往设置页面配置。";
            await OpenSettings();
            return;
        }

        var targetPath = SelectedMod.ModPath;

        if (!forceRescan && _modItemsCache.TryGetValue(targetPath, out var cached))
        {
            BindItems(cached, editable: true, snapshotOnly: false);
            SelectedMod.Status = "就绪(缓存)";
            CurrentDataState = "已加载（缓存），可编辑。";
            return;
        }

        try
        {
            SelectedMod.Status = "扫描中";
            LogOutput = $"正在扫描 {SelectedMod.Name} ...";

            var scannedItems = await Task.Run(() => _modParserService.ScanModFolder(targetPath));
            _modItemsCache[targetPath] = scannedItems;
            NotifyLoadActionStateChanged();

            if (SelectedMod?.ModPath == targetPath)
            {
                BindItems(scannedItems, editable: true, snapshotOnly: false);
                CurrentDataState = "已加载（实时扫描），可编辑。";
            }

            if (SelectedMod?.ModPath == targetPath)
            {
                SelectedMod.Status = "就绪";
                SelectedMod.ItemCount = scannedItems.Count;
                SelectedMod.LastScanAt = DateTime.Now.ToString("HH:mm:ss");
            }

            LogOutput = scannedItems.Count == 0
                ? "扫描完成，但未发现可翻译条目。"
                : $"扫描完成，共加载 {scannedItems.Count} 条翻译项。";
        }
        catch (Exception ex)
        {
            if (SelectedMod?.ModPath == targetPath)
            {
                SelectedMod.Status = "失败";
            }
            LogOutput = $"扫描失败: {ex.Message}";
        }
    }

    private void BindItems(IReadOnlyList<TranslationItem> items, bool editable, bool snapshotOnly)
    {
        _allItems = items.ToList();

        UpdateVersionList();
        if (!AvailableVersions.Contains(SelectedVersion))
        {
            SelectedVersion = "全部";
        }

        ApplyFilter();
        IsTranslationTableReadOnly = !editable;
        IsCurrentModLoaded = editable;

        if (snapshotOnly)
        {
            CurrentDataState = "缓存快照（只读）。";
        }
    }

    private void ShowEmptyDataState(string message)
    {
        _allItems.Clear();
        TranslationItems.Clear();
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");
        SelectedVersion = "全部";
        IsTranslationTableReadOnly = true;
        IsCurrentModLoaded = false;
        CurrentDataState = message;
    }

    private void UpdateVersionList()
    {
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");

        var versions = _allItems
            .Select(x => string.IsNullOrEmpty(x.Version) ? "根目录" : x.Version)
            .Distinct()
            .OrderBy(x => x);

        foreach (var version in versions)
        {
            AvailableVersions.Add(version);
        }
    }

    private void ApplyFilter()
    {
        TranslationItems.Clear();
        if (_allItems.Count == 0) return;

        IEnumerable<TranslationItem> filtered;
        if (string.IsNullOrEmpty(SelectedVersion) || SelectedVersion == "全部")
        {
            filtered = _allItems;
        }
        else
        {
            var targetVersion = SelectedVersion == "根目录" ? "" : SelectedVersion;
            filtered = _allItems.Where(x => x.Version == targetVersion);
        }

        foreach (var item in filtered)
        {
            TranslationItems.Add(item);
        }

        if (SelectedMod != null)
        {
            LogOutput = $"{SelectedMod.Name}: 显示 {TranslationItems.Count}/{_allItems.Count} 条 (版本: {SelectedVersion})";
        }
    }

    [RelayCommand]
    private async Task StartTranslation()
    {
        if (SelectedMod == null)
        {
            LogOutput = "请先在中栏选择一个 Mod。";
            return;
        }

        if (!IsCurrentModLoaded)
        {
            LogOutput = "请先点击“加载翻译条目”，再执行翻译。";
            return;
        }

        if (TranslationItems.Count == 0)
        {
            LogOutput = "当前列表为空，请先切换版本或刷新数据。";
            return;
        }

        var config = _configService.CurrentConfig;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            LogOutput = "错误：未配置 API Key，请点击“程序设置”进行配置。";
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ApiUrl))
        {
            LogOutput = "错误：未配置 API URL，请点击“程序设置”进行配置。";
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.TargetModel))
        {
            LogOutput = "错误：未配置目标模型，请点击“程序设置”进行配置。";
            await OpenSettings();
            return;
        }

        IsTranslating = true;

        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource();
        var cancellationToken = _translationCts.Token;

        var allItems = TranslationItems.ToList();

        var distinctGroups = allItems
            .GroupBy(x => x.OriginalText)
            .ToList();

        int totalGroups = distinctGroups.Count;
        int totalItems = allItems.Count;
        int processedGroups = 0;

        LogOutput = $"开始翻译：{SelectedMod.Name} 共 {totalItems} 条，去重后 {totalGroups} 条文本。";
        LogOutput += $"\n模型: {config.TargetModel} | 目标: {config.TargetLanguage}";

        var batchResult = _batchingService.CreateBatches(
            distinctGroups,
            config.MaxTokensPerBatch,
            config.MinItemsPerBatch,
            config.MaxItemsPerBatch
        );

        var batches = batchResult.Batches;
        int totalBatches = batchResult.TotalBatches;

        LogOutput += $"\n智能分批: {totalBatches} 批次";

        try
        {
            if (config.EnableMultiThreadTranslation)
            {
                await ExecuteMultiThreadTranslationAsync(batchResult, config, cancellationToken, totalBatches);
            }
            else
            {
                for (int i = 0; i < batches.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogOutput += $"\n翻译已停止，完成 {i}/{totalBatches} 批次";
                        break;
                    }

                    var batch = batches[i];
                    int batchTokens = batchResult.BatchTokenCounts[i];

                    var batchDict = batch.ToDictionary(group => group.Key, group => group.Key);

                    try
                    {
                        var results = await _llmService.TranslateBatchAsync(
                            config.ApiKey,
                            batchDict,
                            config.ApiUrl,
                            config.TargetModel,
                            config.TargetLanguage,
                            config.UseCustomPrompt && !string.IsNullOrWhiteSpace(config.CustomPrompt)
                                ? config.CustomPrompt
                                : null,
                            config.ApiRequestTimeoutSeconds,
                            cancellationToken
                        );

                        foreach (var group in batch)
                        {
                            var originalText = group.Key;

                            if (results.TryGetValue(originalText, out string? translatedText) &&
                                !string.IsNullOrWhiteSpace(translatedText))
                            {
                                foreach (var item in group)
                                {
                                    item.TranslatedText = translatedText;
                                    item.Status = "已翻译";
                                }
                            }
                            else
                            {
                                foreach (var item in group)
                                {
                                    if (string.IsNullOrEmpty(item.TranslatedText))
                                        item.Status = "AI跳过";
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOutput += $"\n批次 {i + 1} 失败: {ex.Message}";
                        foreach (var group in batch)
                        {
                            foreach (var item in group) item.Status = "出错";
                        }
                    }

                    processedGroups += batch.Count;
                    LogOutput = $"翻译进度: {i + 1}/{totalBatches} 批次 | {processedGroups}/{totalGroups} 文本 (~{batchTokens} tokens)";
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                LogOutput += "\n翻译任务全部完成！";
            }
        }
        catch (Exception ex)
        {
            LogOutput += $"\n发生致命错误: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translationCts?.Dispose();
            _translationCts = null;
        }
    }

    [RelayCommand]
    private void StopTranslation()
    {
        if (_translationCts == null || _translationCts.IsCancellationRequested)
            return;

        _translationCts.Cancel();
        LogOutput += "\n正在停止翻译...";
    }

    private async Task ExecuteMultiThreadTranslationAsync(
        BatchingService.BatchResult batchResult,
        AppConfig config,
        CancellationToken cancellationToken,
        int totalBatches)
    {
        using var concurrencyManager = new ConcurrencyManager(
            config.MaxThreads,
            config.ThreadIntervalMs);

        using var progressReporter = new ThreadSafeProgressReporter(
            UpdateMultiThreadProgress,
            AppendLogLine);

        using var multiThreadService = new MultiThreadedTranslationService();

        progressReporter.ReportLog($"开始多线程翻译: {config.MaxThreads} 线程, {totalBatches} 批次");

        try
        {
            await multiThreadService.ExecuteBatchesAsync(
                batchResult,
                concurrencyManager,
                progressReporter,
                config.ApiKey,
                config.ApiUrl,
                config.TargetModel,
                config.TargetLanguage,
                config.ApiRequestTimeoutSeconds,
                config.UseCustomPrompt && !string.IsNullOrWhiteSpace(config.CustomPrompt) ? config.CustomPrompt : null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progressReporter.ReportLog("多线程翻译已取消");
            throw;
        }
    }

    private void UpdateMultiThreadProgress(TranslationProgress progress)
    {
        RunOnUiThread(() =>
        {
            LatestLogLine =
                $"多线程翻译进度: {progress.ProcessedBatches}/{progress.TotalBatches} 批次 | 正在运行 {progress.ActiveThreads} 个线程";
        });
    }

    private void AppendLogLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            LogOutput = string.IsNullOrWhiteSpace(LogOutput)
                ? message
                : $"{LogOutput}\n{message}";
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    [RelayCommand]
    private async Task SaveFiles()
    {
        if (SelectedMod == null)
        {
            LogOutput = "请先选择一个 Mod。";
            return;
        }

        if (!IsCurrentModLoaded)
        {
            LogOutput = "请先加载翻译条目，再执行保存。";
            return;
        }

        if (TranslationItems.Count == 0 || string.IsNullOrEmpty(_currentModPath))
        {
            LogOutput = "当前没有可保存的条目。";
            return;
        }

        LogOutput = "正在生成 XML 文件...";
        string targetLang = _configService.CurrentConfig.TargetLanguage;

        try
        {
            int count = await Task.Run(() =>
                _fileGeneratorService.GenerateFiles(_currentModPath, targetLang, _allItems));

            LogOutput = $"保存成功！已在 Languages/{targetLang} 下生成 {count} 个文件。";

            await Task.Run(() =>
            {
                string version = string.IsNullOrEmpty(SelectedVersion) || SelectedVersion == "全部"
                    ? ""
                    : SelectedVersion;

                string packageId = _currentPackageId ?? "UnknownMod";
                string modName = _currentModName ?? SelectedMod.Name;

                var backupPath = _backupService.BackupTranslationFolder(
                    _currentModPath,
                    modName,
                    packageId,
                    version,
                    targetLang);

                if (backupPath != null)
                {
                    LogOutput = $"{LogOutput}\n已自动创建备份。";
                }
            });
        }
        catch (Exception ex)
        {
            LogOutput = $"保存失败: {ex.Message}";
        }
    }

    private void LoadModInfo(string modFolderPath)
    {
        try
        {
            var modInfo = _modInfoService.LoadModInfo(modFolderPath);
            if (modInfo != null)
            {
                _currentPackageId = modInfo.PackageId;
                _currentModName = modInfo.Name;

                ModInfoViewModel = new ModInfoViewModel();
                ModInfoViewModel.LoadFromModInfo(modInfo);
            }
            else
            {
                ModInfoViewModel = null;
                _currentPackageId = null;
                _currentModName = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"加载 Mod 信息失败: {ex.Message}");
            ModInfoViewModel = null;
        }
    }
}
