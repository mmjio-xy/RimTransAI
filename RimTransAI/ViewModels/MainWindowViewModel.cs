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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Views;

namespace RimTransAI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string InitialLogMessage = "就绪。请先在设置页配置 Mod 来源目录。";
    private readonly ConfigService _configService;
    private readonly FileGeneratorService _fileGeneratorService;
    private readonly LlmService _llmService;
    private readonly ModParserService _modParserService;
    private readonly BatchingService _batchingService;
    private readonly ModInfoService _modInfoService;
    private readonly BackupService _backupService;
    private readonly WorkspaceService _workspaceService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly Dictionary<string, List<TranslationItem>> _modItemsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkspaceModItem> _allWorkspaceMods = [];

    private List<TranslationItem> _allItems = new();
    private string _currentModPath = string.Empty;
    private string? _currentPackageId;
    private string? _currentModName;
    private CancellationTokenSource? _translationCts;
    private readonly OperationLogBuffer _operationLogBuffer;

    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private string _logOutput = InitialLogMessage;
    [ObservableProperty] private string _latestLogLine = InitialLogMessage;
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
    public ReadOnlyObservableCollection<OperationLogEntry> OperationLogs => _operationLogBuffer.Entries;
    public string AppDisplayTitle { get; } = BuildAppDisplayTitle();

    private static string BuildAppDisplayTitle()
    {
        var version = typeof(global::RimTransAI.App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return $"RimTrans AI v{version}";
    }

    partial void OnLogOutputChanged(string value)
    {
        LatestLogLine = value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? value;
    }

    public MainWindowViewModel()
    {
        _loggerFactory = NullLoggerFactory.Instance;
        _logger = NullLogger<MainWindowViewModel>.Instance;
        _operationLogBuffer = new OperationLogBuffer(capacity: 500);
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
        WorkspaceService workspaceService,
        ILoggerFactory loggerFactory,
        OperationLogBuffer operationLogBuffer)
    {
        _modParserService = modParserService;
        _llmService = llmService;
        _fileGeneratorService = fileGeneratorService;
        _configService = configService;
        _batchingService = batchingService;
        _modInfoService = modInfoService;
        _backupService = backupService;
        _workspaceService = workspaceService;
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<MainWindowViewModel>();
        _operationLogBuffer = operationLogBuffer ?? throw new ArgumentNullException(nameof(operationLogBuffer));

        InitializeCollections();
        LoadWorkspaceFromConfig();
    }

    private void InitializeCollections()
    {
        _operationLogBuffer.EntryAdded += OnOperationLogEntryAdded;
        _logger.LogUserInformation(InitialLogMessage);
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");
        SelectedVersion = "全部";
        ModSearchModeIndex = 0;
    }

    private void OnOperationLogEntryAdded(object? sender, OperationLogEntry entry)
    {
        LatestLogLine = entry.Message;
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
            BindItems(cachedItems, editable: true, snapshotOnly: false);
            CurrentDataState = "已加载（缓存），可编辑。";
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
            _logger.LogUserWarning("未发现 Mod，请在设置页添加有效的来源目录");
            return;
        }

        ApplyWorkspaceFilter(previousPath);

        LogOutput = $"已发现 {_allWorkspaceMods.Count} 个 Mod，可在中栏选择后手动加载翻译条目。";
        _logger.LogUserInformation("已发现 {ModCount} 个 Mod，可选择后加载翻译条目", _allWorkspaceMods.Count);
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
            _logger.LogUserWarning("请先选择一个 Mod");
            return;
        }

        var config = _configService.CurrentConfig;
        if (string.IsNullOrWhiteSpace(config.AssemblyCSharpPath))
        {
            LogOutput = "错误：未配置 Assembly-CSharp.dll 路径，请先前往设置页面配置。";
            _logger.LogUserWarning("未配置 Assembly-CSharp.dll 路径，请先前往设置页面配置");
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

        using var scanScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = Guid.NewGuid().ToString("N"),
            ["OperationType"] = "Scan",
            ["ModName"] = SelectedMod.Name,
            ["ModPath"] = targetPath
        });

        try
        {
            _logger.LogUserInformation("开始扫描 Mod：{ModName}", SelectedMod.Name);
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
            var scanDiagnostics = _modParserService.LastScanDiagnostics;
            var scanPartiallyFailed = scanDiagnostics.ExtractionErrorCount > 0 ||
                                      scanDiagnostics.LoadFolderFallbackDueToError;
            if (scanPartiallyFailed)
            {
                if (SelectedMod?.ModPath == targetPath)
                {
                    SelectedMod.Status = "部分完成";
                }

                LogOutput = $"扫描部分完成：加载 {scannedItems.Count} 条翻译项，发现 {scanDiagnostics.ExtractionErrorCount} 个提取错误。";
                _logger.LogUserWarning(
                    "扫描部分完成：加载 {ItemCount} 条翻译项，提取错误 {ExtractionErrorCount} 个，目录规划回退：{LoadFolderFallbackDueToError}",
                    scannedItems.Count,
                    scanDiagnostics.ExtractionErrorCount,
                    scanDiagnostics.LoadFolderFallbackDueToError);
            }
            else if (scannedItems.Count == 0)
            {
                _logger.LogUserWarning("扫描完成，但未发现可翻译条目");
            }
            else
            {
                _logger.LogUserSuccess("扫描完成，共加载 {ItemCount} 条翻译项", scannedItems.Count);
            }
        }
        catch (Exception ex)
        {
            if (SelectedMod?.ModPath == targetPath)
            {
                SelectedMod.Status = "失败";
            }
            LogOutput = $"扫描失败: {ex.Message}";
            _logger.LogUserError(ex, "扫描 Mod 失败：{ErrorMessage}", ex.Message);
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
            _logger.LogUserWarning("请先在中栏选择一个 Mod");
            return;
        }

        if (!IsCurrentModLoaded)
        {
            LogOutput = "请先点击“加载翻译条目”，再执行翻译。";
            _logger.LogUserWarning("请先加载翻译条目，再执行翻译");
            return;
        }

        if (TranslationItems.Count == 0)
        {
            LogOutput = "当前列表为空，请先切换版本或刷新数据。";
            _logger.LogUserWarning("当前翻译列表为空，请先切换版本或刷新数据");
            return;
        }

        var config = _configService.CurrentConfig;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            LogOutput = "错误：未配置 API Key，请点击“程序设置”进行配置。";
            _logger.LogUserWarning("未配置 API Key，请前往程序设置");
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ApiUrl))
        {
            LogOutput = "错误：未配置 API URL，请点击“程序设置”进行配置。";
            _logger.LogUserWarning("未配置 API URL，请前往程序设置");
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.TargetModel))
        {
            LogOutput = "错误：未配置目标模型，请点击“程序设置”进行配置。";
            _logger.LogUserWarning("未配置目标模型，请前往程序设置");
            await OpenSettings();
            return;
        }

        var operationId = Guid.NewGuid().ToString("N");
        using var translationScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = operationId,
            ["OperationType"] = "Translation",
            ["ModName"] = SelectedMod.Name,
            ["Model"] = config.TargetModel
        });

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
        int successfulBatches = 0;

        _logger.LogUserInformation(
            "开始翻译：{ModName}，共 {ItemCount} 条，去重后 {DistinctTextCount} 条文本",
            SelectedMod.Name,
            totalItems,
            totalGroups);

        var batchResult = _batchingService.CreateBatches(
            distinctGroups,
            config.MaxTokensPerBatch,
            config.MinItemsPerBatch,
            config.MaxItemsPerBatch
        );

        var batches = batchResult.Batches;
        int totalBatches = batchResult.TotalBatches;

        _logger.LogDebug(
            "翻译分批完成 TotalBatches={TotalBatches} OversizedBatches={OversizedBatches} ApiUrl={ApiUrl}",
            totalBatches,
            batchResult.OversizedBatches,
            config.ApiUrl);
        LogOutput = $"开始翻译：{SelectedMod.Name} 共 {totalItems} 条，去重后 {totalGroups} 条文本。";
        LogOutput += $"\n模型: {config.TargetModel} | 目标: {config.TargetLanguage}";
        LogOutput += $"\n智能分批: {totalBatches} 批次";

        try
        {
            if (config.EnableMultiThreadTranslation)
            {
                successfulBatches = await ExecuteMultiThreadTranslationAsync(
                    batchResult,
                    config,
                    cancellationToken,
                    totalBatches);
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
                            config.AutoCompleteApiUrl,
                            cancellationToken
                        );

                        var missingCount = 0;
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
                                missingCount++;
                                foreach (var item in group)
                                {
                                    if (string.IsNullOrEmpty(item.TranslatedText))
                                        item.Status = "AI跳过";
                                }
                            }
                        }

                        if (missingCount == 0)
                        {
                            successfulBatches++;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "单线程翻译批次结果不完整 BatchIndex={BatchIndex} TotalBatches={TotalBatches} MissingCount={MissingCount}",
                                i + 1,
                                totalBatches,
                                missingCount);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogUserError(
                            ex,
                            "翻译批次 {BatchIndex}/{TotalBatches} 失败：{ErrorMessage}",
                            i + 1,
                            totalBatches,
                            ex.Message);
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
                LogOutput += successfulBatches == totalBatches
                    ? "\n翻译任务全部完成！"
                    : $"\n翻译结束：成功 {successfulBatches}/{totalBatches} 批次，其余批次失败。";

                if (successfulBatches == totalBatches)
                {
                    _logger.LogUserSuccess("翻译任务全部完成，共 {TotalBatches} 个批次", totalBatches);
                }
                else
                {
                    _logger.LogUserWarning(
                        "翻译结束：成功 {SuccessfulBatches}/{TotalBatches} 个批次",
                        successfulBatches,
                        totalBatches);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogUserWarning(
                "翻译已取消，完成 {SuccessfulBatches}/{TotalBatches} 个批次",
                successfulBatches,
                totalBatches);
            LogOutput += "\n翻译已取消。";
        }
        catch (Exception ex)
        {
            _logger.LogUserError(ex, "翻译过程发生致命错误：{ErrorMessage}", ex.Message);
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
        _logger.LogUserWarning("正在停止翻译");
    }

    private async Task<int> ExecuteMultiThreadTranslationAsync(
        BatchingService.BatchResult batchResult,
        AppConfig config,
        CancellationToken cancellationToken,
        int totalBatches)
    {
        using var concurrencyManager = new ConcurrencyManager(
            config.MaxThreads,
            config.ThreadIntervalMs,
            _loggerFactory.CreateLogger<ConcurrencyManager>());

        using var progressReporter = new ThreadSafeProgressReporter(
            UpdateMultiThreadProgress,
            message => _logger.LogDebug("{TranslationProgressMessage}", message));

        using var multiThreadService = new MultiThreadedTranslationService(
            _llmService,
            RunOnUiThreadAsync,
            _loggerFactory.CreateLogger<MultiThreadedTranslationService>());

        progressReporter.ReportLog($"开始多线程翻译: {config.MaxThreads} 线程, {totalBatches} 批次");

        try
        {
            return await multiThreadService.ExecuteBatchesAsync(
                batchResult,
                concurrencyManager,
                progressReporter,
                config.ApiKey,
                config.ApiUrl,
                config.TargetModel,
                config.TargetLanguage,
                config.ApiRequestTimeoutSeconds,
                config.UseCustomPrompt && !string.IsNullOrWhiteSpace(config.CustomPrompt) ? config.CustomPrompt : null,
                config.AutoCompleteApiUrl,
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

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    [RelayCommand]
    private async Task SaveFiles()
    {
        if (SelectedMod == null)
        {
            LogOutput = "请先选择一个 Mod。";
            _logger.LogUserWarning("请先选择一个 Mod");
            return;
        }

        if (!IsCurrentModLoaded)
        {
            LogOutput = "请先加载翻译条目，再执行保存。";
            _logger.LogUserWarning("请先加载翻译条目，再执行保存");
            return;
        }

        if (TranslationItems.Count == 0 || string.IsNullOrEmpty(_currentModPath))
        {
            LogOutput = "当前没有可保存的条目。";
            _logger.LogUserWarning("当前没有可保存的翻译条目");
            return;
        }

        LogOutput = "正在生成 XML 文件...";
        string targetLang = _configService.CurrentConfig.TargetLanguage;
        using var saveScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = Guid.NewGuid().ToString("N"),
            ["OperationType"] = "SaveTranslation",
            ["ModName"] = SelectedMod.Name,
            ["TargetLanguage"] = targetLang
        });

        try
        {
            _logger.LogUserInformation("开始生成翻译文件，共 {ItemCount} 条翻译项", _allItems.Count);
            var generationResult = await Task.Run(() =>
                _fileGeneratorService.GenerateFilesDetailed(_currentModPath, targetLang, _allItems));
            int count = generationResult.SuccessfulFileCount;

            LogOutput = count == 0 && generationResult.IsCompleteSuccess
                ? "没有已翻译条目，因此未生成文件。"
                : generationResult.IsCompleteSuccess
                    ? $"保存成功！已在 Languages/{targetLang} 下生成 {count} 个文件。"
                    : $"部分保存成功：生成 {count} 个文件，另有失败项，请查看日志。";

            IReadOnlyList<string> backupPaths = [];
            var backupVersionCount = generationResult.SuccessfulVersions.Count;
            var autoBackupEnabled = _configService.CurrentConfig.EnableAutoBackup;
            if (count > 0 && backupVersionCount > 0)
            {
                backupPaths = await Task.Run(() =>
                {
                    string packageId = _currentPackageId ?? "UnknownMod";
                    string modName = _currentModName ?? SelectedMod.Name;

                    return _backupService.BackupTranslationFolders(
                        _currentModPath,
                        modName,
                        packageId,
                        generationResult.SuccessfulVersions,
                        targetLang);
                });
            }

            if (backupPaths.Count > 0)
            {
                LogOutput += $"\n已自动创建 {backupPaths.Count} 个版本备份。";
            }
            else if (count > 0 && autoBackupEnabled)
            {
                LogOutput += "\n自动备份未创建，请查看日志确认目标目录。";
            }

            if (count == 0 && generationResult.IsCompleteSuccess)
            {
                _logger.LogUserWarning("没有已翻译条目，本次未生成翻译文件");
            }
            else if (generationResult.IsCompleteSuccess)
            {
                if (!autoBackupEnabled)
                {
                    _logger.LogUserSuccess(
                        "翻译文件生成完成，共生成 {GeneratedFileCount} 个文件；自动备份未启用",
                        count);
                }
                else if (backupPaths.Count == backupVersionCount)
                {
                    _logger.LogUserSuccess(
                        "翻译文件生成完成，共生成 {GeneratedFileCount} 个文件；已创建 {BackupCount} 个版本备份",
                        count,
                        backupPaths.Count);
                }
                else
                {
                    _logger.LogUserWarning(
                        "翻译文件生成完成，共生成 {GeneratedFileCount} 个文件；自动备份仅创建 {CreatedBackupCount}/{ExpectedBackupCount} 个",
                        count,
                        backupPaths.Count,
                        backupVersionCount);
                }
            }
            else
            {
                _logger.LogUserWarning(
                    "翻译文件部分生成成功：成功 {SuccessfulFileCount} 个文件，失败 {FailedFileCount} 个文件，失败节点 {FailedNodeCount} 个",
                    generationResult.SuccessfulFileCount,
                    generationResult.FailedFileCount,
                    generationResult.FailedNodeCount);
            }
        }
        catch (Exception ex)
        {
            LogOutput = $"保存失败: {ex.Message}";
            _logger.LogUserError(ex, "生成翻译文件失败：{ErrorMessage}", ex.Message);
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

                ModInfoViewModel = new ModInfoViewModel(
                    _loggerFactory.CreateLogger<ModInfoViewModel>());
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
            _logger.LogWarning(ex, "加载 Mod 信息失败 ModFolderPath={ModFolderPath}", modFolderPath);
            ModInfoViewModel = null;
        }
    }
}
