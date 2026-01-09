using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage; // 用于文件选择器
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Models;
using RimTransAI.Services;

namespace RimTransAI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ModParserService _modParserService;
    private readonly LlmService _llmService;
    
    private readonly FileGeneratorService _fileGeneratorService;

    // 1. 数据源：存放扫描到的所有原始数据
    private List<TranslationItem> _allItems = new();

    // 2. 视图源：绑定到 DataGrid，会根据筛选条件变化
    public ObservableCollection<TranslationItem> TranslationItems { get; } = new();

    // 3. 版本列表：绑定到 ComboBox
    public ObservableCollection<string> AvailableVersions { get; } = new();

    // 4. UI 绑定属性
    [ObservableProperty] 
    private string _selectedVersion = "全部";

    [ObservableProperty] 
    private string _apiKey = string.Empty;

    [ObservableProperty] 
    private string _logOutput = "就绪。请选择 Mod 文件夹开始...";

    // 进度条相关
    [ObservableProperty] 
    private double _progressValue = 0;

    [ObservableProperty] 
    private bool _isTranslating = false;

    // =========================================================
    // 构造函数
    // =========================================================

    // 1. 设计时预览用的无参构造函数 (避免 XAML 设计器报错)
    public MainWindowViewModel(ModParserService modParserService, LlmService llmService, FileGeneratorService fileGeneratorService)
    {
        _modParserService = new ModParserService();
        _llmService = new LlmService();
        _fileGeneratorService = fileGeneratorService;
    }

    // 2. 运行时依赖注入构造函数
    public MainWindowViewModel(ModParserService modParserService, LlmService llmService)
    {
        _modParserService = modParserService;
        _llmService = llmService;
    }

    // =========================================================
    // 核心逻辑
    // =========================================================

    // 监听 SelectedVersion 变化，自动触发筛选
    partial void OnSelectedVersionChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 选择文件夹并扫描 Mod 内容
    /// </summary>
    [RelayCommand]
    private async Task SelectFolder()
    {
        // 获取当前窗口以弹出选择框
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
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
        _currentModPath = selectedPath;
        LogOutput = $"正在扫描: {selectedPath}";

        // 后台扫描，避免卡顿界面
        try 
        {
            _allItems = await Task.Run(() => _modParserService.ScanModFolder(selectedPath));
            
            if (_allItems.Count == 0)
            {
                LogOutput = "未找到有效的 XML 语言文件或 Defs 文件。";
                return;
            }

            // 更新版本下拉框
            UpdateVersionList();

            // 默认重置为“全部”并刷新显示
            SelectedVersion = "全部";
            ApplyFilter();

            LogOutput = $"扫描完成！共找到 {_allItems.Count} 条数据。";
        }
        catch (Exception ex)
        {
            LogOutput = $"扫描出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 更新下拉框的版本列表
    /// </summary>
    private void UpdateVersionList()
    {
        AvailableVersions.Clear();
        AvailableVersions.Add("全部");

        // 提取所有出现的版本号，去重并排序
        var versions = _allItems
            .Select(x => string.IsNullOrEmpty(x.Version) ? "根目录" : x.Version)
            .Distinct()
            .OrderBy(x => x);

        foreach (var v in versions)
        {
            AvailableVersions.Add(v);
        }
    }

    /// <summary>
    /// 根据当前选中的版本筛选显示内容
    /// </summary>
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
            // 处理 "根目录" 和空字符串的对应关系
            var targetVersion = SelectedVersion == "根目录" ? "" : SelectedVersion;
            filtered = _allItems.Where(x => x.Version == targetVersion);
        }

        foreach (var item in filtered)
        {
            TranslationItems.Add(item);
        }
        
        // 更新日志提示当前数量
        if (_allItems.Count > 0)
        {
            LogOutput = $"显示: {TranslationItems.Count} / {_allItems.Count} 条 (版本: {SelectedVersion})";
        }
    }

    /// <summary>
    /// 开始批量翻译
    /// </summary>
    [RelayCommand]
    private async Task StartTranslation()
    {
        // 基本检查
        if (TranslationItems.Count == 0)
        {
            LogOutput = "当前列表为空，请先加载 Mod 或切换版本。";
            return;
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            LogOutput = "错误：请输入 API Key";
            return;
        }

        IsTranslating = true;
        ProgressValue = 0;
        
        // 1. 获取待处理列表
        // 注意：这里我们翻译当前 UI 列表中所有显示的项
        var pendingItems = TranslationItems.ToList(); 
        int total = pendingItems.Count;
        int processed = 0;
        
        // 2. 批处理设置 (每批 20 条，避免 Token 溢出)
        int batchSize = 20;
        var chunks = pendingItems.Chunk(batchSize).ToList();

        LogOutput = $"开始翻译：共 {total} 条，分 {chunks.Count} 批次处理...";

        try
        {
            // 3. 循环处理每一批
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                
                // 构造 Dictionary<Key, Text> 发送给 AI
                // 如果 Key 重复（虽然在 XML 里不应该重复），ToDictionary 会报错，所以用 Distinct 保护一下
                var batchDict = chunk
                    .GroupBy(x => x.Key)
                    .ToDictionary(g => g.Key, g => g.First().OriginalText);

                try
                {
                    // 调用 LLM Service
                    var results = await _llmService.TranslateBatchAsync(ApiKey, batchDict);

                    // 回填结果
                    foreach (var item in chunk)
                    {
                        if (results.TryGetValue(item.Key, out string? translated))
                        {
                            item.TranslatedText = translated;
                            item.Status = "已翻译";
                        }
                        else
                        {
                            // 偶尔 AI 可能会漏掉某些 Key
                            item.Status = "AI跳过"; 
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogOutput += $"\n批次 {i+1} 失败: {ex.Message}";
                    foreach(var item in chunk) item.Status = "出错";
                }

                // 更新进度
                processed += chunk.Length;
                ProgressValue = (double)processed / total * 100;
                
                // 实时更新日志，让用户知道没卡死
                LogOutput = $"翻译进度: {processed}/{total} ({ProgressValue:F1}%)";
            }
            
            LogOutput += "\n翻译任务全部完成！";
        }
        catch (Exception ex)
        {
            LogOutput += $"\n发生致命错误: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
        }
    }
    
    /// <summary>
    /// 保存文件
    /// </summary>
    [RelayCommand]
    private async Task SaveFiles()
    {
        if (TranslationItems.Count == 0) return;

        // 假设 ModParserService 扫描时记录了 RootPath，或者我们重新获取
        // 简单起见，我们假设用户没有在扫描后移动文件夹
        // 更好的做法是在 ViewModel 里存一个 _currentModPath 变量
        if (_allItems.Count == 0) return;
    
        // 从第一条数据反推 Mod 根目录有点麻烦，
        // 建议在 SelectFolder 方法里就把 selectedPath 存到一个类成员变量里
        if (string.IsNullOrEmpty(_currentModPath)) 
        {
            LogOutput = "错误：无法确定 Mod 路径，请重新选择文件夹。";
            return;
        }

        LogOutput = "正在生成 XML 文件...";

        // 获取目标语言 (从 ComboBox 获取，这里先硬编码演示，你可以绑定属性)
        string targetLang = "ChineseSimplified"; 

        int count = await Task.Run(() => _fileGeneratorService.GenerateFiles(_currentModPath, targetLang, _allItems));

        LogOutput = $"保存成功！已生成 {count} 个 XML 文件。\n请检查 Mod 文件夹下的 Languages/{targetLang} 目录。";
    }

// 记得在类里加这个字段用来存路径
    private string _currentModPath = string.Empty;
}