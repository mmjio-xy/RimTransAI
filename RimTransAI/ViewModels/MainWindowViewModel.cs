using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Views;

// 需要这一行来解析 SettingsViewModel

// 引用 Views 命名空间以使用 SettingsWindow

namespace RimTransAI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigService _configService; // 新增配置服务
    private readonly FileGeneratorService _fileGeneratorService;
    private readonly LlmService _llmService;
    private readonly ModParserService _modParserService;
    private readonly BatchingService _batchingService; // 智能分批服务

    // 1. 数据源：存放扫描到的所有原始数据
    private List<TranslationItem> _allItems = new();

    // 内部状态：记录当前 Mod 路径
    private string _currentModPath = string.Empty;

    // 翻译取消令牌源
    private CancellationTokenSource? _translationCts;

    [ObservableProperty] private bool _isTranslating = false;

    // 移除 ApiKey 属性，现在从 ConfigService 获取

    [ObservableProperty] private string _logOutput = "就绪。请选择 Mod 文件夹开始...";

    // 进度条相关
    [ObservableProperty] private double _progressValue = 0;

    // 4. UI 绑定属性
    [ObservableProperty] private string _selectedVersion = "全部";

    // =========================================================
    // 构造函数
    // =========================================================

    // 设计时构造函数
    public MainWindowViewModel()
    {
        // 设计时初始化所有服务，避免空引用异常
        var reflectionAnalyzer = new ReflectionAnalyzer();
        _configService = new ConfigService();
        _modParserService = new ModParserService(reflectionAnalyzer, _configService);
        _llmService = new LlmService();
        _fileGeneratorService = new FileGeneratorService();
        _batchingService = new BatchingService();

        // 初始化版本列表，避免设计器报错
        AvailableVersions.Add("全部");
    }

    // 运行时构造函数 (注入所有服务)
    public MainWindowViewModel(
        ModParserService modParserService,
        LlmService llmService,
        FileGeneratorService fileGeneratorService,
        ConfigService configService,
        BatchingService batchingService)
    {
        _modParserService = modParserService;
        _llmService = llmService;
        _fileGeneratorService = fileGeneratorService;
        _configService = configService;
        _batchingService = batchingService;
    }

    // 2. 视图源：绑定到 DataGrid
    public ObservableCollection<TranslationItem> TranslationItems { get; } = new();

    // 3. 版本列表：绑定到 ComboBox
    public ObservableCollection<string> AvailableVersions { get; } = new();

    // =========================================================
    // 核心逻辑
    // =========================================================

    partial void OnSelectedVersionChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 打开设置窗口
    /// </summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        // 获取当前主窗口
        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        // 从 App.Services 中获取 SettingsViewModel 的新实例
        var app = (App)Application.Current!;
        if (app.Services == null) return;

        var settingsVm = app.Services.GetRequiredService<SettingsViewModel>();

        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };

        // 模态显示设置窗口
        await settingsWindow.ShowDialog(topLevel);

        // 窗口关闭后提示
        LogOutput += "\n设置已更新。";
    }

    /// <summary>
    /// 选择 Mod 文件夹
    /// </summary>
    [RelayCommand]
    private async Task SelectFolder()
    {
        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 RimWorld Mod 根目录",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var selectedPath = folders[0].Path.LocalPath;
        _currentModPath = selectedPath; // 记录路径
        LogOutput = $"正在扫描: {selectedPath}";

        try
        {
            // 检查是否配置了 Assembly-CSharp.dll
            var config = _configService.CurrentConfig;
            if (string.IsNullOrWhiteSpace(config.AssemblyCSharpPath))
            {
                LogOutput = "错误：未配置 Assembly-CSharp.dll 路径\n";
                LogOutput += "请先点击【参数设置】按钮，配置 Assembly-CSharp.dll 的路径\n";
                LogOutput += "该文件通常位于：\n";
                LogOutput += "Steam: steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll";
                await OpenSettings();
                return;
            }

            _allItems = await Task.Run(() => _modParserService.ScanModFolder(selectedPath));

            if (_allItems.Count == 0)
            {
                LogOutput = "未找到有效的翻译数据。\n";
                LogOutput += "可能的原因：\n";
                LogOutput += "1. Mod 目录下没有 Assemblies 文件夹\n";
                LogOutput += "2. Assembly-CSharp.dll 路径配置不正确\n";
                LogOutput += "3. Mod DLL 文件无法加载";
                return;
            }

            UpdateVersionList();
            SelectedVersion = "全部";
            ApplyFilter();

            LogOutput = $"扫描完成！共找到 {_allItems.Count} 条数据。";
        }
        catch (Exception ex)
        {
            LogOutput = $"扫描出错: {ex.Message}";
        }
    }

    private void UpdateVersionList()
    {
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");

        var versions = _allItems
            .Select(x => string.IsNullOrEmpty(x.Version) ? "根目录" : x.Version)
            .Distinct()
            .OrderBy(x => x);

        foreach (var v in versions)
        {
            AvailableVersions.Add(v);
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

        if (_allItems.Count > 0)
        {
            LogOutput = $"显示: {TranslationItems.Count} / {_allItems.Count} 条 (版本: {SelectedVersion})";
        }
    }

    /// <summary>
    /// 开始翻译 (自动去重优化版)
    /// </summary>
    [RelayCommand]
    private async Task StartTranslation()
    {
        if (TranslationItems.Count == 0)
        {
            LogOutput = "当前列表为空，请先加载 Mod 或切换版本。";
            return;
        }

        var config = _configService.CurrentConfig;

        // 验证所有必需的配置项
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            LogOutput = "错误：未配置 API Key，请点击\"参数设置\"按钮进行配置。";
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ApiUrl))
        {
            LogOutput = "错误：未配置 API URL，请点击\"参数设置\"按钮进行配置。";
            await OpenSettings();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.TargetModel))
        {
            LogOutput = "错误：未配置目标模型，请点击\"参数设置\"按钮进行配置。";
            await OpenSettings();
            return;
        }

        IsTranslating = true;
        ProgressValue = 0;

        // 创建新的取消令牌
        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource();
        var cancellationToken = _translationCts.Token;

        // 1. 获取当前视图中所有需要翻译的条目
        // (不管是“未翻译”还是“已翻译”想重翻，都包含在内)
        var allItems = TranslationItems.ToList();

        // 2. 按“原文”进行分组去重
        // key: 原文, value: 拥有该原文的所有条目列表
        var distinctGroups = allItems
            .GroupBy(x => x.OriginalText)
            .ToList();

        int totalGroups = distinctGroups.Count; // 实际需要翻译的唯一文本数量
        int totalItems = allItems.Count; // 总条目数
        int processedGroups = 0;
        int coveredItems = 0; // 累计已覆盖的条目数

        LogOutput = $"开始翻译：共 {totalItems} 条目，去重后需翻译 {totalGroups} 条文本...";
        LogOutput += $"\n模型: {config.TargetModel} | 目标: {config.TargetLanguage}";

        // 使用智能分批服务
        var batchResult = _batchingService.CreateBatches(
            distinctGroups,
            config.MaxTokensPerBatch,
            config.MinItemsPerBatch,
            config.MaxItemsPerBatch
        );

        var batches = batchResult.Batches;
        int totalBatches = batchResult.TotalBatches;

        // 日志输出分批统计
        LogOutput += $"\n智能分批: {totalBatches} 批次";
        if (batchResult.OversizedBatches > 0)
            LogOutput += $" (含 {batchResult.OversizedBatches} 个超长文本单独处理)";

        try
        {
            for (int i = 0; i < batches.Count; i++)
            {
                // 检查是否请求取消
                if (cancellationToken.IsCancellationRequested)
                {
                    LogOutput += $"\n翻译已停止，完成 {i}/{totalBatches} 批次";
                    break;
                }

                var batch = batches[i];
                int batchTokens = batchResult.BatchTokenCounts[i];

                // 3. 构建发送给 AI 的字典
                var batchDict = batch.ToDictionary(
                    group => group.Key,
                    group => group.Key
                );

                try
                {
                    // 调用 LLM
                    var results = await _llmService.TranslateBatchAsync(
                        config.ApiKey,
                        batchDict,
                        config.ApiUrl,
                        config.TargetModel,
                        config.TargetLanguage
                    );

                    // 4. 结果回填 (关键步骤)
                    foreach (var group in batch)
                    {
                        // 使用原文作为 Key 查找翻译结果
                        var originalText = group.Key;

                        // 检查 AI 是否返回了翻译
                        if (results.TryGetValue(originalText, out string? translatedText) &&
                            !string.IsNullOrWhiteSpace(translatedText))
                        {
                            // 广播：把翻译结果赋给该组下的【所有】条目
                            foreach (var item in group)
                            {
                                item.TranslatedText = translatedText;
                                item.Status = "已翻译";
                            }
                        }
                        else
                        {
                            // AI 没返回或跳过
                            foreach (var item in group)
                            {
                                // 如果原来没翻译，标记为跳过；如果原来有翻译，保持不变
                                if (string.IsNullOrEmpty(item.TranslatedText))
                                    item.Status = "AI跳过";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogOutput += $"\n批次 {i + 1} 失败: {ex.Message}";
                    // 标记该批次所有条目为出错
                    foreach (var group in batch)
                    {
                        foreach (var item in group) item.Status = "出错";
                    }
                }

                processedGroups += batch.Count;
                // 累加本批次覆盖的条目数
                coveredItems += batch.Sum(g => g.Count());

                // 进度条按"处理的唯一文本组数"计算
                ProgressValue = (double)processedGroups / totalGroups * 100;

                // 日志显示优化 - 显示批次和 Token 信息
                LogOutput = $"翻译进度: {i + 1}/{totalBatches} 批次 | {processedGroups}/{totalGroups} 文本 (~{batchTokens} tokens)";
            }

            // 区分正常完成和取消完成
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

    /// <summary>
    /// 停止翻译
    /// </summary>
    [RelayCommand]
    private void StopTranslation()
    {
        if (_translationCts == null || _translationCts.IsCancellationRequested)
            return;

        _translationCts.Cancel();
        LogOutput += "\n正在停止翻译...";
    }

    /// <summary>
    /// 保存文件
    /// </summary>
    [RelayCommand]
    private async Task SaveFiles()
    {
        if (TranslationItems.Count == 0 || string.IsNullOrEmpty(_currentModPath)) return;

        LogOutput = "正在生成 XML 文件...";

        // 从配置获取目标语言文件夹名 (如 ChineseSimplified)
        string targetLang = _configService.CurrentConfig.TargetLanguage;

        try
        {
            int count =
                await Task.Run(() => _fileGeneratorService.GenerateFiles(_currentModPath, targetLang, _allItems));
            LogOutput = $"保存成功！已在 Languages/{targetLang} 下生成 {count} 个文件。";
        }
        catch (Exception ex)
        {
            LogOutput = $"保存失败: {ex.Message}";
        }
    }
}