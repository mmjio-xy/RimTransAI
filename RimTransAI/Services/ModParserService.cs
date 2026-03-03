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
using RimTransAI.Services.Scanning;

namespace RimTransAI.Services;

public class ModParserService
{
    // 用于匹配路径中的版本号（带斜杠）
    private static readonly Regex VersionRegex = new Regex(@"[\\/](\d+\.\d+)[\\/]", RegexOptions.Compiled);

    // 用于匹配目录名中的版本号（不带斜杠）
    private static readonly Regex VersionDirRegex = new Regex(@"^\d+\.\d+$", RegexOptions.Compiled);

    // 兼容旧版 RimWorld 语言目录命名
    private static readonly string[] KeyedFolderNames = { "Keyed", "CodeLinked" };

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
    private readonly ScanOrchestrator _scanOrchestrator;
    private Dictionary<string, HashSet<string>>? _reflectionMap;

    // 构造函数：必须提供反射分析器和配置服务
    public ModParserService(ReflectionAnalyzer reflectionAnalyzer, ConfigService configService)
    {
        _reflectionAnalyzer = reflectionAnalyzer ?? throw new ArgumentNullException(nameof(reflectionAnalyzer));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _scanOrchestrator = new ScanOrchestrator();
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

        // 阶段 0：重构入口已落位。阶段 5 会将主流程切换到 _scanOrchestrator.Scan(...)。
        _ = _scanOrchestrator;

        // 第一步：加载类型定义（Core + Mod DLL）
        Logger.Info("步骤 1/4: 加载类型定义...");
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
        var loadFolders = ResolveLoadFoldersForScan(modPath);
        Logger.Info($"加载目录解析完成: {loadFolders.Count} 个");
        foreach (var loadFolder in loadFolders)
        {
            Logger.Debug($"[LoadFolder] {loadFolder.RelativePath} (版本: {loadFolder.Version})");
        }

        var defXmlEntries = CollectDefsXmlEntries(loadFolders);
        int totalXmlFiles = defXmlEntries.Count;
        int processedFiles = 0;
        int skippedFiles = 0;
        int validDefFiles = 0;
        Logger.Info($"找到 {totalXmlFiles} 个 Defs XML 文件");

        // 优化：并行处理 XML 文件，使用 ConcurrentBag 线程安全收集结果
        var itemsConcurrent = new System.Collections.Concurrent.ConcurrentBag<TranslationItem>();
        object lockObj = new object();

        // 【重构】并行处理 XML 文件，带根节点验证
        Parallel.ForEach(defXmlEntries, entry =>
        {
            var file = entry.FilePath;
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

                string version = entry.Version;

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
        var keyedItems = ScanKeyedFiles(loadFolders);
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

    private readonly record struct LoadFolderContext(string FolderPath, string RelativePath, string Version);
    private readonly record struct ScanFileEntry(string FilePath, string Version);

    /// <summary>
    /// 解析当前 Mod 实际参与加载的目录集合（近似对齐 foldersToLoadDescendingOrder）。
    /// </summary>
    private List<LoadFolderContext> ResolveLoadFoldersForScan(string modPath)
    {
        var result = new List<LoadFolderContext>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPackageIds = GetLikelyActivePackageIds(modPath);

        void TryAddFolder(string relativePath, string source)
        {
            var normalized = NormalizeRelativePath(relativePath);
            var isRoot = string.IsNullOrEmpty(normalized) || normalized == ".";
            var folderPath = isRoot ? modPath : Path.Combine(modPath, normalized);
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(folderPath);
            if (!seenFolders.Add(fullPath))
            {
                return;
            }

            var folderVersion = InferVersionFromRelativePath(normalized);
            result.Add(new LoadFolderContext(
                fullPath,
                isRoot ? "." : normalized,
                folderVersion));
            Logger.Debug($"[LoadFolders] + {relativePath} (来源: {source}, 版本: {folderVersion})");
        }

        // 1) 解析 LoadFolders.xml（若存在）
        var loadFoldersFile = Path.Combine(modPath, "LoadFolders.xml");
        if (File.Exists(loadFoldersFile))
        {
            foreach (var (relativePath, conditionMatched) in ParseLoadFoldersXml(loadFoldersFile, knownPackageIds))
            {
                if (!conditionMatched)
                {
                    Logger.Debug($"[LoadFolders] 条件未命中，仍保留目录（避免漏扫）: {relativePath}");
                }

                TryAddFolder(relativePath, "LoadFolders.xml");
            }
        }

        // 2) 版本目录（按版本号降序）
        var versionDirectories = Directory
            .EnumerateDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && VersionDirRegex.IsMatch(name))
            .OrderByDescending(ParseVersionDirectory)
            .ToList();

        foreach (var versionDir in versionDirectories)
        {
            TryAddFolder(versionDir!, "VersionDir");
        }

        // 3) Common 目录
        TryAddFolder("Common", "Default");

        // 4) 根目录
        TryAddFolder(".", "Default");

        return result;
    }

    private static List<(string RelativePath, bool ConditionMatched)> ParseLoadFoldersXml(
        string loadFoldersPath,
        HashSet<string> knownPackageIds)
    {
        var result = new List<(string RelativePath, bool ConditionMatched)>();

        try
        {
            var doc = XDocument.Load(loadFoldersPath);
            if (doc.Root == null)
            {
                return result;
            }

            foreach (var item in doc.Descendants().Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
            {
                var relativePath = item.Value?.Trim();
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var conditionMatched = EvaluateLoadFolderConditions(item, knownPackageIds);
                result.Add((relativePath, conditionMatched));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"解析 LoadFolders.xml 失败: {ex.Message}");
        }

        return result;
    }

    private static bool EvaluateLoadFolderConditions(XElement folderElement, HashSet<string> knownPackageIds)
    {
        bool hasCondition = false;
        bool matched = true;

        var ifModActive = folderElement.Attribute("IfModActive")?.Value;
        if (!string.IsNullOrWhiteSpace(ifModActive))
        {
            hasCondition = true;
            var packages = SplitPackageIds(ifModActive);
            matched &= packages.Any(id => knownPackageIds.Contains(id));
        }

        var ifModActiveAll = folderElement.Attribute("IfModActiveAll")?.Value;
        if (!string.IsNullOrWhiteSpace(ifModActiveAll))
        {
            hasCondition = true;
            var packages = SplitPackageIds(ifModActiveAll);
            matched &= packages.All(id => knownPackageIds.Contains(id));
        }

        var ifModNotActive = folderElement.Attribute("IfModNotActive")?.Value;
        if (!string.IsNullOrWhiteSpace(ifModNotActive))
        {
            hasCondition = true;
            var packages = SplitPackageIds(ifModNotActive);
            matched &= packages.All(id => !knownPackageIds.Contains(id));
        }

        return !hasCondition || matched;
    }

    private static string[] SplitPackageIds(string value)
    {
        return value
            .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Version ParseVersionDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Version(0, 0);
        }

        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/')
            .TrimEnd('/');
    }

    private static string InferVersionFromRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var firstSegment = NormalizeRelativePath(relativePath).Split('/')[0];
        return VersionDirRegex.IsMatch(firstSegment) ? firstSegment : string.Empty;
    }

    private static HashSet<string> GetLikelyActivePackageIds(string modPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core 包通常始终处于启用状态
            "ludeon.rimworld"
        };

        var aboutPath = Path.Combine(modPath, "About", "About.xml");
        if (!File.Exists(aboutPath))
        {
            return result;
        }

        try
        {
            var doc = XDocument.Load(aboutPath);
            if (doc.Root == null)
            {
                return result;
            }

            var packageId = doc.Root.Element("packageId")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                result.Add(packageId);
            }

            var dependencies = doc.Root
                .Element("modDependencies")?
                .Elements("li")
                .Select(li => li.Element("packageId")?.Value?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id));

            if (dependencies != null)
            {
                foreach (var dependency in dependencies)
                {
                    result.Add(dependency!);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"读取 About.xml 依赖信息失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 按加载目录顺序收集 Defs XML（同版本+同相对路径只保留首个，模拟同模组文件去重语义）。
    /// </summary>
    private List<ScanFileEntry> CollectDefsXmlEntries(IReadOnlyList<LoadFolderContext> loadFolders)
    {
        var entries = new List<ScanFileEntry>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var loadFolder in loadFolders)
        {
            var defsDir = Path.Combine(loadFolder.FolderPath, "Defs");
            if (!Directory.Exists(defsDir))
            {
                continue;
            }

            var xmlFiles = new ConcurrentBag<string>();
            CollectXmlFilesRecursively(defsDir, xmlFiles);

            foreach (var file in xmlFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(loadFolder.FolderPath, file));
                var dedupeKey = $"{loadFolder.Version}|{relativePath}";
                if (!seenFiles.Add(dedupeKey))
                {
                    Logger.Debug($"[Defs 去重跳过] {relativePath} (版本: {loadFolder.Version})");
                    continue;
                }

                entries.Add(new ScanFileEntry(file, loadFolder.Version));
            }
        }

        return entries;
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
    private List<TranslationItem> ScanKeyedFiles(IReadOnlyList<LoadFolderContext> loadFolders)
    {
        Logger.Info($"[Keyed 扫描] 开始扫描 Keyed 文件...");

        var items = new List<TranslationItem>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int processedFiles = 0;
        foreach (var loadFolder in loadFolders)
        {
            foreach (var languageDir in ResolveEnglishLanguageFolders(loadFolder.FolderPath))
            {
                foreach (var keyedFolderName in KeyedFolderNames)
                {
                    var keyedDir = Path.Combine(languageDir, keyedFolderName);
                    if (!Directory.Exists(keyedDir))
                    {
                        continue;
                    }

                    Logger.Debug($"[Keyed 扫描] 正在处理目录: {keyedDir} (版本: {loadFolder.Version})");
                    foreach (var file in Directory.EnumerateFiles(keyedDir, "*.xml", SearchOption.AllDirectories)
                                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var relativePath = NormalizeRelativePath(Path.GetRelativePath(loadFolder.FolderPath, file));
                        var dedupeKey = $"{loadFolder.Version}|{relativePath}";
                        if (!seenFiles.Add(dedupeKey))
                        {
                            Logger.Debug($"[Keyed 去重跳过] {relativePath} (版本: {loadFolder.Version})");
                            continue;
                        }

                        var fileItems = ParseKeyedFile(file, loadFolder.Version);
                        items.AddRange(fileItems);
                        if (fileItems.Count > 0)
                        {
                            processedFiles++;
                            Logger.Debug($"[Keyed 扫描] {Path.GetFileName(file)}: 提取 {fileItems.Count} 个翻译项");
                        }
                    }
                }
            }
        }

        Logger.Info($"Keyed 文件处理完成: {processedFiles} 个文件，{items.Count} 个翻译项");
        return items;
    }

    private static IEnumerable<string> ResolveEnglishLanguageFolders(string loadFolderPath)
    {
        var languagesRoot = Path.Combine(loadFolderPath, "Languages");
        if (!Directory.Exists(languagesRoot))
        {
            return Enumerable.Empty<string>();
        }

        var englishDir = Path.Combine(languagesRoot, "English");
        if (Directory.Exists(englishDir))
        {
            return new[] { englishDir };
        }

        // 兼容 legacyFolderName 场景：当不存在 English 时，回退到名称中包含 English 的目录。
        return Directory
            .EnumerateDirectories(languagesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(dir => Path.GetFileName(dir).Contains("English", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

            var seenKeysInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in doc.Root.Elements())
            {
                // 黑名单过滤
                if (IsKeyedElementFiltered(element)) continue;

                string key = element.Name.LocalName;
                if (!seenKeysInFile.Add(key))
                {
                    Logger.Warning($"Keyed 文件内重复 Key，已跳过后续项: {key} ({Path.GetFileName(filePath)})");
                    continue;
                }

                string text = (element.Value ?? string.Empty)
                    .Replace("\\n", "\n")
                    .Trim();

                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Equals("TODO", StringComparison.OrdinalIgnoreCase)) continue;

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
