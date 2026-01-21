using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class ModParserService
{
    // 用于匹配路径中的版本号（带斜杠）
    private static readonly Regex VersionRegex = new Regex(@"[\\/](\d+\.\d+)[\\/]", RegexOptions.Compiled);

    // 用于匹配目录名中的版本号（不带斜杠）
    private static readonly Regex VersionDirRegex = new Regex(@"^\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>
    /// 目录黑名单（文件遍历时直接跳过，防止扫描无效文件导致性能问题）
    /// </summary>
    private static readonly HashSet<string> DirectoryBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // A. 资源类（绝对没有 XML 代码）
        "Textures", "Sounds", "Materials", "Meshes", "Music",

        // B. 系统与元数据类（XML 不是用来翻译的）
        "About", "Languages", "News",

        // C. 开发垃圾文件（如果你扫描的是源码版 Mod）
        ".git", ".vs", ".idea", "bin", "obj", "Source"
    };

    private readonly ConfigService _configService;
    private readonly ReflectionAnalyzer _reflectionAnalyzer;
    private Dictionary<string, HashSet<string>>? _reflectionMap;

    // 性能优化：版本路径缓存（文件路径 -> 版本号）
    private Dictionary<string, string>? _versionCache;

    // 构造函数：必须提供反射分析器和配置服务
    public ModParserService(ReflectionAnalyzer reflectionAnalyzer, ConfigService configService)
    {
        _reflectionAnalyzer = reflectionAnalyzer ?? throw new ArgumentNullException(nameof(reflectionAnalyzer));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public List<TranslationItem> ScanModFolder(string modPath)
    {
        var items = new List<TranslationItem>();

        if (!Directory.Exists(modPath))
        {
            Logger.Error($"Mod 目录不存在: {modPath}");
            return items;
        }

        Logger.Info("========================================");
        Logger.Info("开始扫描 Mod 文件夹");
        Logger.Info($"路径: {modPath}");
        Logger.Info("========================================");

        // 第一步：加载类型定义（Core + Mod DLL）
        Logger.Info("步骤 1/3: 加载类型定义...");
        _reflectionMap = TryAnalyzeModAssemblies(modPath);

        if (_reflectionMap == null || _reflectionMap.Count == 0)
        {
            Logger.Error("========================================");
            Logger.Error("错误：无法加载核心类型定义");
            Logger.Error("可能的原因：");
            Logger.Error("1. 未配置 Assembly-CSharp.dll 路径");
            Logger.Error("2. Assembly-CSharp.dll 文件不存在或损坏");
            Logger.Error("========================================");
            Logger.Info("请先在【参数设置】中配置 Assembly-CSharp.dll 的路径");
            Logger.Info("Assembly-CSharp.dll 通常位于：");
            Logger.Info("  Steam: steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll");
            Logger.Error("========================================");
            return items;
        }

        Logger.Info($"类型定义加载完成，找到 {_reflectionMap.Count} 个可翻译类型");

        // 第二步：创建翻译提取器
        Logger.Info("步骤 2/4: 创建翻译提取器...");
        var extractor = new TranslationExtractor(_reflectionMap);

        // 第三步：扫描并解析 XML 文件（模拟 RimWorld 加载 Defs）
        Logger.Info("步骤 3/4: 扫描 Defs XML 文件...");

        // 【重构】使用递归遍历（带黑名单剪枝）收集所有 XML 文件
        var allXmlFiles = new ConcurrentBag<string>();
        CollectXmlFilesRecursively(modPath, allXmlFiles);

        int totalXmlFiles = allXmlFiles.Count;
        int processedFiles = 0;
        int skippedFiles = 0;
        int validDefFiles = 0;
        Logger.Info($"找到 {totalXmlFiles} 个 XML 文件");

        // [性能优化] 步骤 2.5/4: 构建版本路径缓存
        Logger.Debug("步骤 2.5/4: 构建版本路径缓存...");
        _versionCache = BuildVersionCache(modPath);

        // 优化：并行处理 XML 文件，使用 ConcurrentBag 线程安全收集结果
        var itemsConcurrent = new System.Collections.Concurrent.ConcurrentBag<TranslationItem>();
        object lockObj = new object();

        // 【重构】并行处理 XML 文件，带根节点验证
        Parallel.ForEach(allXmlFiles, file =>
        {
            try
            {
                // 【新增】步骤 1：快速验证根节点类型（最小代价）
                var rootNodeName = GetXmlRootNodeName(file);
                if (rootNodeName == null)
                {
                    Interlocked.Increment(ref skippedFiles);
                    return;
                }

                // 【新增】步骤 2：验证是否为有效的 Defs 文件
                if (!IsValidDefFile(rootNodeName))
                {
                    Logger.Debug($"[跳过] {Path.GetFileName(file)} (根节点: {rootNodeName})");
                    Interlocked.Increment(ref skippedFiles);
                    return;
                }

                Interlocked.Increment(ref validDefFiles);

                // 步骤 3：完整加载 XML 文件
                var doc = XDocument.Load(file);
                if (doc.Root == null) return;

                // [性能优化] 从缓存获取版本号，O(1) 查找
                string version = _versionCache != null && _versionCache.TryGetValue(file, out var cachedVer)
                    ? cachedVer
                    : GetVersionFromPath(file);

                // 优化：直接遍历元素，避免 ToList() 内存分配
                var units = extractor.Extract(doc.Root.Elements(), file, version);

                // 转换为 TranslationItem
                foreach (var unit in units)
                {
                    itemsConcurrent.Add(new TranslationItem
                    {
                        Key = unit.Key,
                        DefType = unit.DefType,
                        OriginalText = unit.OriginalText,
                        TranslatedText = "",
                        Status = "未翻译",
                        FilePath = unit.SourceFile,
                        Version = unit.Version
                    });
                }

                Interlocked.Increment(ref processedFiles);

                // 每处理 50 个文件输出一次进度
                if (processedFiles % 50 == 0)
                {
                    Logger.Info($"已处理: {processedFiles}/{totalXmlFiles} 个文件");
                }
            }
            catch (XmlException ex)
            {
                Interlocked.Increment(ref skippedFiles);
                lock (lockObj)
                {
                    Logger.Warning($"XML格式错误 {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref skippedFiles);
                lock (lockObj)
                {
                    Logger.Error($"解析文件出错 {Path.GetFileName(file)}", ex);
                }
            }
        });

        Logger.Info($"Defs 扫描完成: 有效 Defs 文件 {validDefFiles} 个，处理 {processedFiles} 个，跳过 {skippedFiles} 个");

        // 将并发收集的项转换为 List
        items.AddRange(itemsConcurrent);

        // 第四步：扫描 Keyed 文件
        Logger.Info("步骤 4/4: 扫描 Keyed 文件...");
        var keyedItems = ScanKeyedFiles(modPath);
        items.AddRange(keyedItems);

        Logger.Info("========================================");
        Logger.Info("扫描完成");
        Logger.Info($"Defs 翻译项: {items.Count - keyedItems.Count} 个");
        Logger.Info($"Keyed 翻译项: {keyedItems.Count} 个");
        Logger.Info($"总计: {items.Count} 个");
        Logger.Info("========================================");

        return items;
    }

    /// <summary>
    /// 解析单个XML文件
    /// 模拟 RimWorld 加载单个 Def 文件的过程
    /// </summary>
    public List<TranslationItem> ParseSingleFile(string filePath)
    {
        var items = new List<TranslationItem>();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件不存在", filePath);
        }

        if (!filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("只支持XML文件", nameof(filePath));
        }

        Logger.Info("========================================");
        Logger.Info("解析单个 XML 文件");
        Logger.Info($"文件: {Path.GetFileName(filePath)}");
        Logger.Info("========================================");

        // 第一步：获取文件所在的Mod目录
        string? modPath = GetModRootPath(filePath);
        if (modPath == null)
        {
            throw new InvalidOperationException("无法确定Mod根目录");
        }

        Logger.Info($"Mod 根目录: {modPath}");

        // 第二步：加载类型定义（Core + Mod DLL）
        Logger.Info("步骤 1/2: 加载类型定义...");
        _reflectionMap = TryAnalyzeModAssemblies(modPath);

        if (_reflectionMap == null || _reflectionMap.Count == 0)
        {
            Logger.Error("无法加载核心类型定义，请检查 Assembly-CSharp.dll 配置");
            return items;
        }

        Logger.Info($"类型定义加载完成，找到 {_reflectionMap.Count} 个可翻译类型");

        // 第三步：创建翻译提取器并解析文件
        Logger.Info("步骤 2/2: 解析 XML 文件...");
        var extractor = new TranslationExtractor(_reflectionMap);

        try
        {
            var doc = XDocument.Load(filePath);
            if (doc.Root == null)
            {
                Logger.Warning("XML 文件没有根元素");
                return items;
            }

            string version = GetVersionFromPath(filePath);
            // 优化：直接传递 IEnumerable，避免 ToList()
            var units = extractor.Extract(doc.Root.Elements(), filePath, version);

            foreach (var unit in units)
            {
                items.Add(new TranslationItem
                {
                    Key = unit.Key,
                    DefType = unit.DefType,
                    OriginalText = unit.OriginalText,
                    TranslatedText = "",
                    Status = "未翻译",
                    FilePath = unit.SourceFile,
                    Version = unit.Version
                });
            }

            Logger.Info("========================================");
            Logger.Info($"解析完成，提取 {items.Count} 个翻译项");
            Logger.Info("========================================");
        }
        catch (XmlException ex)
        {
            throw new XmlException($"XML格式错误: {ex.Message}", ex);
        }

        return items;
    }

    /// <summary>
    /// [性能优化] 预构建版本路径缓存
    /// 将文件路径映射到版本号，避免重复的正则匹配
    /// </summary>
    private Dictionary<string, string> BuildVersionCache(string modPath)
    {
        var cache = new Dictionary<string, string>();
        var versionDirs = new List<(string path, string version)>();

        // 1. 检查根目录下的 Defs 和 Patches
        var rootDefsDir = Path.Combine(modPath, "Defs");
        var rootPatchesDir = Path.Combine(modPath, "Patches");

        if (Directory.Exists(rootDefsDir))
        {
            versionDirs.Add((rootDefsDir, ""));
        }
        if (Directory.Exists(rootPatchesDir))
        {
            versionDirs.Add((rootPatchesDir, ""));
        }

        // 2. 检查版本目录下的 Defs 和 Patches
        foreach (var versionDir in Directory.EnumerateDirectories(modPath, "*", SearchOption.TopDirectoryOnly))
        {
            var dirName = Path.GetFileName(versionDir);

            // 跳过黑名单目录
            if (DirectoryBlacklist.Contains(dirName))
            {
                continue;
            }

            if (VersionDirRegex.IsMatch(dirName) ||
                dirName.Equals("Common", StringComparison.OrdinalIgnoreCase))
            {
                var versionDefsDir = Path.Combine(versionDir, "Defs");
                var versionPatchesDir = Path.Combine(versionDir, "Patches");

                if (Directory.Exists(versionDefsDir))
                {
                    versionDirs.Add((versionDefsDir, dirName));
                }
                if (Directory.Exists(versionPatchesDir))
                {
                    versionDirs.Add((versionPatchesDir, dirName));
                }
            }
        }

        // 3. 为每个目录下的 XML 文件缓存版本号（使用带黑名单的递归遍历）
        foreach (var (dirPath, version) in versionDirs)
        {
            // 使用递归遍历，跳过黑名单目录
            var subDirFiles = new ConcurrentBag<string>();
            CollectXmlFilesRecursively(dirPath, subDirFiles);

            // 缓存版本号
            foreach (var file in subDirFiles)
            {
                cache[file] = version;
            }
        }

        Logger.Debug($"[性能优化] 版本缓存构建完成: {cache.Count} 个文件");
        return cache;
    }

    /// <summary>
    /// 从文件路径提取版本号
    /// </summary>
    private string GetVersionFromPath(string filePath)
    {
        var match = VersionRegex.Match(filePath);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return "";
    }

    /// <summary>
    /// 从文件路径获取Mod根目录
    /// </summary>
    private string? GetModRootPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            // 检查是否存在 About 或 Assemblies 目录
            if (Directory.Exists(Path.Combine(dir, "About")) ||
                Directory.Exists(Path.Combine(dir, "Assemblies")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// 尝试分析 Mod 程序集
    /// 模拟 RimWorld 加载和分析 Mod DLL 的过程
    /// 重构后的逻辑：强制加载 Core，可选加载 Mod DLL，始终返回全量数据
    /// </summary>
    private Dictionary<string, HashSet<string>>? TryAnalyzeModAssemblies(string modPath)
    {
        try
        {
            // ========== 第一步：强制加载核心程序集 ==========
            string corePath = _configService.CurrentConfig.AssemblyCSharpPath;
            if (string.IsNullOrWhiteSpace(corePath) || !File.Exists(corePath))
            {
                Logger.Error("错误：未配置或找不到 Assembly-CSharp.dll，请在设置中配置");
                return null;
            }

            try
            {
                _reflectionAnalyzer.LoadCore(corePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"加载核心程序集失败", ex);
                return null;
            }

            // ========== 第二步：尝试加载 Mod DLL（可选）==========
            var allDllFiles = new List<string>();

            Logger.Info("正在查找 Mod DLL 文件...");

            // 2.1 检查根目录下的 Assemblies 文件夹
            var rootAssembliesDir = Path.Combine(modPath, "Assemblies");
            if (Directory.Exists(rootAssembliesDir))
            {
                // 优化：使用 EnumerateFiles
                var rootDlls = Directory.EnumerateFiles(rootAssembliesDir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                allDllFiles.AddRange(rootDlls);
                Logger.Info($"  根目录/Assemblies: {rootDlls.Count} 个 DLL");
            }

            // 2.2 检查版本目录下的 Assemblies 文件夹
            if (Directory.Exists(modPath))
            {
                var versionDirs = Directory.EnumerateDirectories(modPath, "*", SearchOption.TopDirectoryOnly);
                foreach (var versionDir in versionDirs)
                {
                    var dirName = Path.GetFileName(versionDir);

                    // 检查是否为版本目录（如 1.4, 1.5, Common 等）
                    if (VersionDirRegex.IsMatch(dirName) ||
                        dirName.Equals("Common", StringComparison.OrdinalIgnoreCase))
                    {
                        var versionAssembliesDir = Path.Combine(versionDir, "Assemblies");
                        if (Directory.Exists(versionAssembliesDir))
                        {
                            // 优化：使用 EnumerateFiles
                            var versionDlls = Directory.EnumerateFiles(versionAssembliesDir, "*.dll",
                                SearchOption.TopDirectoryOnly).ToList();
                            allDllFiles.AddRange(versionDlls);
                            Logger.Info($"  {dirName}/Assemblies: {versionDlls.Count} 个 DLL");
                        }
                    }
                }
            }

            // 2.3 如果没有找到 DLL，记录日志但不退出
            if (allDllFiles.Count == 0)
            {
                Logger.Info("未找到任何 DLL 文件");
                Logger.Info("纯 XML Mod，使用 Core 数据");
            }
            else
            {
                Logger.Info($"共找到 {allDllFiles.Count} 个 DLL 文件");

                // 第三步：分析所有找到的 DLL 文件
                Logger.Info("正在分析 DLL 文件...");
                int successCount = 0;
                int failCount = 0;

                foreach (var dllFile in allDllFiles)
                {
                    try
                    {
                        // 调用 AnalyzeModAssembly 更新 Analyzer 内部状态
                        // 不需要接收返回值，因为它会更新 _typeFieldsMap
                        _reflectionAnalyzer.AnalyzeModAssembly(dllFile);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"分析程序集失败: {Path.GetFileName(dllFile)}", ex);
                        failCount++;
                    }
                }

                Logger.Info($"DLL 分析完成: 成功 {successCount} 个，失败 {failCount} 个");
            }

            // ========== 第四步：统一返回全量数据 ==========
            // 无论是否找到 DLL，都返回完整的类型字段映射（Core + Mod）
            var allTypeFields = _reflectionAnalyzer.GetAllTypeFields();

            if (allTypeFields.Count > 0)
            {
                int totalFields = allTypeFields.Sum(x => x.Value.Count);
                Logger.Info($"返回 {allTypeFields.Count} 个可翻译类型，共 {totalFields} 个字段");
                return allTypeFields;
            }

            Logger.Warning("未找到任何可翻译的类型");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"扫描 Mod 程序集时出错", ex);
            return null;
        }
    }

    /// <summary>
    /// 扫描 Keyed 文件目录
    /// </summary>
    private List<TranslationItem> ScanKeyedFiles(string modPath)
    {
        Logger.Info($"[Keyed 扫描] 开始扫描 Keyed 文件...");

        var items = new List<TranslationItem>();
        var keyedDirs = new List<(string path, string version)>();

        // 1. 检查根目录下的 Languages/English/Keyed
        var rootKeyedDir = Path.Combine(modPath, "Languages", "English", "Keyed");
        if (Directory.Exists(rootKeyedDir))
        {
            Logger.Debug($"[Keyed 扫描] 找到根目录 Keyed: {rootKeyedDir}");
            keyedDirs.Add((rootKeyedDir, ""));
        }

        // 2. 检查版本目录下的 Languages/English/Keyed
        var versionDirs = Directory.EnumerateDirectories(modPath, "*", SearchOption.TopDirectoryOnly);
        foreach (var versionDir in versionDirs)
        {
            var dirName = Path.GetFileName(versionDir);
            if (VersionDirRegex.IsMatch(dirName) ||
                dirName.Equals("Common", StringComparison.OrdinalIgnoreCase))
            {
                var versionKeyedDir = Path.Combine(versionDir, "Languages", "English", "Keyed");
                if (Directory.Exists(versionKeyedDir))
                {
                    Logger.Debug($"[Keyed 扫描] 找到版本目录 Keyed: {versionKeyedDir}");
                    keyedDirs.Add((versionKeyedDir, dirName));
                }
            }
        }

        if (keyedDirs.Count == 0)
        {
            Logger.Info("未找到 Keyed 目录");
            return items;
        }

        Logger.Info($"找到 {keyedDirs.Count} 个 Keyed 目录");

        // 3. 解析每个目录下的 XML 文件
        int processedFiles = 0;
        foreach (var (keyedDir, version) in keyedDirs)
        {
            Logger.Debug($"[Keyed 扫描] 正在处理目录: {keyedDir} (版本: {version})");
            // 优化：使用 EnumerateFiles
            var xmlFiles = Directory.EnumerateFiles(keyedDir, "*.xml", SearchOption.AllDirectories);
            foreach (var file in xmlFiles)
            {
                Logger.Debug($"[Keyed 扫描] 正在解析文件: {Path.GetFileName(file)}");
                var fileItems = ParseKeyedFile(file, version);
                items.AddRange(fileItems);
                if (fileItems.Count > 0)
                {
                    processedFiles++;
                    Logger.Debug($"[Keyed 扫描] {Path.GetFileName(file)}: 提取 {fileItems.Count} 个翻译项");
                }
            }
        }

        Logger.Info($"Keyed 文件处理完成: {processedFiles} 个文件，{items.Count} 个翻译项");
        return items;
    }

    /// <summary>
    /// 解析单个 Keyed XML 文件
    /// </summary>
    private List<TranslationItem> ParseKeyedFile(string filePath, string version)
    {
        var items = new List<TranslationItem>();

        try
        {
            var doc = XDocument.Load(filePath);
            if (doc.Root == null) return items;

            // 验证根元素是否为 LanguageData
            if (doc.Root.Name.LocalName != "LanguageData")
            {
                Logger.Debug($"跳过非 LanguageData 文件: {Path.GetFileName(filePath)}");
                return items;
            }

            foreach (var element in doc.Root.Elements())
            {
                // 黑名单过滤
                if (IsKeyedElementFiltered(element)) continue;

                string key = element.Name.LocalName;
                string text = element.Value?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(text)) continue;

                items.Add(new TranslationItem
                {
                    Key = key,
                    DefType = "Keyed",
                    OriginalText = text,
                    TranslatedText = "",
                    Status = "未翻译",
                    FilePath = filePath,
                    Version = version
                });
            }
        }
        catch (XmlException ex)
        {
            Logger.Warning($"Keyed XML 格式错误 {Path.GetFileName(filePath)}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error($"解析 Keyed 文件出错 {Path.GetFileName(filePath)}", ex);
        }

        return items;
    }

    /// <summary>
    /// 判断 Keyed 元素是否应被过滤
    /// </summary>
    private bool IsKeyedElementFiltered(XElement element)
    {
        // 跳过有子元素的节点（非叶子节点）
        if (element.HasElements) return true;

        // 跳过空内容
        if (string.IsNullOrWhiteSpace(element.Value)) return true;

        return false;
    }

    /// <summary>
    /// 递归遍历目录，收集 XML 文件（带黑名单剪枝）
    /// </summary>
    /// <param name="directory">当前目录</param>
    /// <param name="xmlFiles">收集的 XML 文件列表</param>
    private static void CollectXmlFilesRecursively(string directory, ConcurrentBag<string> xmlFiles)
    {
        try
        {
            // 步骤 1：检查目录名是否在黑名单中（黑名单剪枝）
            var dirName = Path.GetFileName(directory);
            if (DirectoryBlacklist.Contains(dirName))
            {
                Logger.Debug($"[黑名单跳过] 目录: {dirName}");
                return; // 直接跳过整个目录树
            }

            // 步骤 2：收集当前目录下的 XML 文件
            foreach (var file in Directory.EnumerateFiles(directory, "*.xml"))
            {
                xmlFiles.Add(file);
            }

            // 步骤 3：递归遍历子目录
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                CollectXmlFilesRecursively(subDir, xmlFiles);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning($"[权限拒绝] 无法访问目录: {directory} - {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[目录遍历错误] {directory}", ex);
        }
    }

    /// <summary>
    /// 快速读取 XML 文件的根节点名称（最小代价验证）
    /// </summary>
    /// <param name="filePath">XML 文件路径</param>
    /// <returns>根节点名称，如果失败返回 null</returns>
    private static string? GetXmlRootNodeName(string filePath)
    {
        try
        {
            // 使用 XmlReader 快速读取根节点（避免完整加载 XDocument）
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, // 忽略 DTD，提升性能
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CloseInput = true
            };

            using var reader = XmlReader.Create(filePath, settings);

            // 快速移动到根元素
            reader.MoveToContent();

            // 返回根节点名称
            return reader.LocalName;
        }
        catch (XmlException ex)
        {
            Logger.Debug($"[XML格式错误] {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[读取文件失败] {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 验证 XML 文件是否为有效的 Defs 文件
    /// </summary>
    /// <param name="rootNodeName">根节点名称</param>
    /// <returns>true 表示是有效的 Defs 文件，false 表示跳过</returns>
    private static bool IsValidDefFile(string rootNodeName)
    {
        // 命中目标：游戏数据文件
        if (rootNodeName.Equals("Defs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 跳过：About.xml
        if (rootNodeName.Equals("ModMetaData", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 跳过：现有汉化文件
        if (rootNodeName.Equals("LanguageData", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 跳过：Patch 补丁文件（通常不包含直接可翻译 Key）
        return (rootNodeName.Equals("Patch", StringComparison.OrdinalIgnoreCase) ||
                rootNodeName.Equals("Operation", StringComparison.OrdinalIgnoreCase)) && false;
        // 其他：跳过
    }
}