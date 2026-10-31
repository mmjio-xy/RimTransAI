using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;
using RimTransAI.Services.Scanning;

namespace RimTransAI.Services;

public class ModParserService
{
    // 用于匹配目录名中的版本号（不带斜杠）
    private static readonly Regex VersionDirRegex = new Regex(@"^\d+\.\d+$", RegexOptions.Compiled);

    private readonly ConfigService _configService;
    private readonly ReflectionAnalyzer _reflectionAnalyzer;
    private readonly ScanOrchestrator _scanOrchestrator;
    private readonly ILogger<ModParserService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Dictionary<string, HashSet<string>>? _reflectionMap;
    public ScanDiagnostics LastScanDiagnostics { get; private set; } = new();

    // 构造函数：必须提供反射分析器和配置服务
    public ModParserService(
        ReflectionAnalyzer reflectionAnalyzer,
        ConfigService configService,
        ILogger<ModParserService>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _reflectionAnalyzer = reflectionAnalyzer ?? throw new ArgumentNullException(nameof(reflectionAnalyzer));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? NullLogger<ModParserService>.Instance;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _scanOrchestrator = new ScanOrchestrator(
            loadOrderPlanner: new GameLoadOrderPlanner(
                _loggerFactory.CreateLogger<GameLoadOrderPlanner>()),
            defFieldExtractionEngine: new DefFieldExtractionEngine(
                logger: _loggerFactory.CreateLogger<DefFieldExtractionEngine>()));
    }

    public List<TranslationItem> ScanModFolder(string modPath)
    {
        var items = new List<TranslationItem>();
        LastScanDiagnostics = new ScanDiagnostics();

        if (!Directory.Exists(modPath))
        {
            LastScanDiagnostics.ExtractionErrorCount = 1;
            _logger.ErrorMessage($"Mod 目录不存在: {modPath}");
            return items;
        }

        _logger.InfoMessage("========================================");
        _logger.InfoMessage("开始扫描 Mod 文件夹");
        _logger.InfoMessage($"路径: {modPath}");
        _logger.InfoMessage("========================================");

        // 第一步：加载类型定义（Core + Mod DLL）
        _logger.InfoMessage("步骤 1/4: 加载类型定义...");
        _reflectionMap = TryAnalyzeModAssemblies(modPath);

        if (_reflectionMap == null || _reflectionMap.Count == 0)
        {
            LastScanDiagnostics.ExtractionErrorCount = 1;
            _logger.ErrorMessage("========================================");
            _logger.ErrorMessage("错误：无法加载核心类型定义");
            _logger.ErrorMessage("可能的原因：");
            _logger.ErrorMessage("1. 未配置 Assembly-CSharp.dll 路径");
            _logger.ErrorMessage("2. Assembly-CSharp.dll 文件不存在或损坏");
            _logger.ErrorMessage("========================================");
            _logger.InfoMessage("请先在【参数设置】中配置 Assembly-CSharp.dll 的路径");
            _logger.InfoMessage("Assembly-CSharp.dll 的位置因游戏版本而异：");
            _logger.InfoMessage($"  {RimWorldAssemblyLocator.ExpectedLayoutsHint}");
            _logger.InfoMessage("  Steam: steamapps/common/RimWorld/ 即为游戏根目录");
            _logger.ErrorMessage("========================================");
            return items;
        }

        _logger.InfoMessage($"类型定义加载完成，找到 {_reflectionMap.Count} 个可翻译类型");

        _logger.InfoMessage("步骤 2/4: 规划加载目录与语言目录...");
        var context = new ScanContext(
            modPath,
            "English",
            "English",
            GetLikelyActivePackageIds(modPath),
            ResolveCurrentGameVersion(modPath));

        _logger.InfoMessage("步骤 3/4: 收集 XML 源文件...");
        _logger.InfoMessage("步骤 4/4: 提取可翻译字段...");
        var scanResult = _scanOrchestrator.Scan(context, _reflectionMap);
        LastScanDiagnostics = scanResult.Diagnostics;
        items.AddRange(scanResult.Items);

        _logger.InfoMessage(
            $"阶段 1/3 目录规划: LoadFolders={scanResult.Diagnostics.LoadFolderCount}, " +
            $"LoadFoldersVersion={(string.IsNullOrEmpty(scanResult.Diagnostics.LoadFolderVersionKey) ? "(目录扫描)" : scanResult.Diagnostics.LoadFolderVersionKey)}, " +
            $"FallbackDueToError={scanResult.Diagnostics.LoadFolderFallbackDueToError}, " +
            $"LanguageDirs={scanResult.Diagnostics.LanguageDirectoryCount}, " +
            $"LanguageArchives={scanResult.Diagnostics.DetectedLanguageArchives.Count}");
        _logger.InfoMessage(
            $"阶段 2/3 文件收集: Defs={scanResult.Diagnostics.DefFileCount}, " +
            $"Keyed={scanResult.Diagnostics.KeyedFileCount}, " +
            $"DefInjected={scanResult.Diagnostics.DefInjectedFileCount}, " +
            $"Backstories={scanResult.Diagnostics.BackstoryFileCount}, " +
            $"Strings={scanResult.Diagnostics.StringFileCount}, " +
            $"WordInfo={scanResult.Diagnostics.WordInfoFileCount}, " +
            $"SourceAttempts={scanResult.Diagnostics.SourceFileAttemptCount}, " +
            $"Registered={scanResult.Diagnostics.SourceFileRegisteredCount}, " +
            $"Deduplicated={scanResult.Diagnostics.SourceFileDeduplicatedCount}");
        _logger.InfoMessage(
            $"阶段 3/3 字段提取: Extracted={scanResult.Diagnostics.ExtractedItemCount}, " +
            $"Conflicts={scanResult.Diagnostics.ExtractionConflictCount}, " +
            $"Errors={scanResult.Diagnostics.ExtractionErrorCount}");

        foreach (var archive in scanResult.Diagnostics.DetectedLanguageArchives)
        {
            _logger.LogUserWarning(
                "检测到语言压缩包：{ArchivePath}。1.7 引擎会优先读取 .tar 并忽略同名目录，" +
                "扫描与写入的散文件都不会生效；请先解包（或删除/重命名该 tar）后重新扫描。",
                archive);
        }

        _logger.InfoMessage("========================================");
        _logger.InfoMessage("扫描完成");
        var keyedCount = items.Count(x => x.DefType == "Keyed");
        _logger.InfoMessage($"Defs 翻译项: {items.Count - keyedCount} 个");
        _logger.InfoMessage($"Keyed 翻译项: {keyedCount} 个");
        _logger.InfoMessage($"总计: {items.Count} 个");
        _logger.InfoMessage("========================================");

        return items;
    }

    /// <summary>
    /// 解析核心程序集路径：优先使用用户配置，失效时自动探测（Mod 目录向上回溯 / Steam / GOG）。
    /// </summary>
    private string? ResolveCoreAssemblyPath(string? hintDirectory)
    {
        var configured = _configService.CurrentConfig.AssemblyCSharpPath;
        var resolved = RimWorldAssemblyLocator.Resolve(configured, hintDirectory);

        if (resolved != null && !RimWorldAssemblyLocator.IsValidAssembly(configured))
        {
            _logger.InfoMessage(string.IsNullOrWhiteSpace(configured)
                ? $"已自动定位核心程序集: {resolved}"
                : $"已配置的 Assembly-CSharp.dll 不可用，自动改用: {resolved}");
        }

        return resolved;
    }

    private string ResolveCurrentGameVersion(string modPath)
    {
        var coreAssemblyPath = ResolveCoreAssemblyPath(modPath);
        if (coreAssemblyPath != null)
        {
            try
            {
                var fileInfo = FileVersionInfo.GetVersionInfo(coreAssemblyPath);
                var productVersion = ExtractMajorMinorVersion(fileInfo.ProductVersion);
                if (!string.IsNullOrWhiteSpace(productVersion))
                {
                    return productVersion;
                }

                var fileVersion = ExtractMajorMinorVersion(fileInfo.FileVersion);
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return fileVersion;
                }
            }
            catch (Exception ex)
            {
                _logger.WarningMessage($"读取 Assembly-CSharp.dll 版本失败: {ex.Message}");
            }

            // 1.7 自研引擎的核心程序集可能不带可用版本信息，回退读取游戏根目录的 Version.txt
            var versionFromFile = TryReadGameVersionFile(coreAssemblyPath);
            if (!string.IsNullOrWhiteSpace(versionFromFile))
            {
                return versionFromFile;
            }
        }

        var fallback = Directory
            .EnumerateDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && VersionDirRegex.IsMatch(name))
            .OrderByDescending(ParseVersionDirectory)
            .FirstOrDefault();

        return fallback ?? string.Empty;
    }

    /// <summary>
    /// 从核心程序集向上查找游戏根目录的 Version.txt（如 "1.7.4990 rev1069"），提取主次版本号。
    /// </summary>
    private static string TryReadGameVersionFile(string coreAssemblyPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(coreAssemblyPath);
            for (var depth = 0; depth < 3 && !string.IsNullOrEmpty(directory); depth++)
            {
                var versionFile = Path.Combine(directory, "Version.txt");
                if (File.Exists(versionFile))
                {
                    var version = ExtractMajorMinorVersion(File.ReadAllText(versionFile));
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (Exception)
        {
            // 版本信息读取失败不影响扫描，继续使用其它回退策略
        }

        return string.Empty;
    }

    private static string ExtractMajorMinorVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return string.Empty;
        }

        var match = Regex.Match(rawVersion, @"\d+\.\d+");
        return match.Success ? match.Value : string.Empty;
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

    private HashSet<string> GetLikelyActivePackageIds(string modPath)
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
            _logger.WarningMessage($"读取 About.xml 依赖信息失败: {ex.Message}");
        }

        return result;
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
            // 1.7 起核心程序集位于游戏根目录；旧版位于 RimWorldWin64_Data\Managed。
            // 配置为空或失效时，尝试从 Mod 目录向上回溯，并探测 Steam/GOG 常见安装位置。
            string? corePath = ResolveCoreAssemblyPath(modPath);
            if (corePath == null)
            {
                _logger.ErrorMessage("错误：未配置或找不到 Assembly-CSharp.dll，请在设置中配置");
                _logger.InfoMessage($"预期位置：{RimWorldAssemblyLocator.ExpectedLayoutsHint}");
                return null;
            }

            try
            {
                _reflectionAnalyzer.LoadCore(corePath);
            }
            catch (Exception ex)
            {
                _logger.ErrorMessage("加载核心程序集失败", ex);
                return null;
            }

            // ========== 第二步：尝试加载 Mod DLL（可选）==========
            var allDllFiles = new List<string>();

            _logger.InfoMessage("正在查找 Mod DLL 文件...");

            // 2.1 检查根目录下的 Assemblies 文件夹
            var rootAssembliesDir = Path.Combine(modPath, "Assemblies");
            if (Directory.Exists(rootAssembliesDir))
            {
                // 优化：使用 EnumerateFiles
                var rootDlls = Directory.EnumerateFiles(rootAssembliesDir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                allDllFiles.AddRange(rootDlls);
                _logger.InfoMessage($"  根目录/Assemblies: {rootDlls.Count} 个 DLL");
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
                            _logger.InfoMessage($"  {dirName}/Assemblies: {versionDlls.Count} 个 DLL");
                        }
                    }
                }
            }

            // 2.3 如果没有找到 DLL，记录日志但不退出
            if (allDllFiles.Count == 0)
            {
                _logger.InfoMessage("未找到任何 DLL 文件");
                _logger.InfoMessage("纯 XML Mod，使用 Core 数据");
            }
            else
            {
                _logger.InfoMessage($"共找到 {allDllFiles.Count} 个 DLL 文件");

                // 第三步：分析所有找到的 DLL 文件
                _logger.InfoMessage("正在分析 DLL 文件...");
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
                        _logger.ErrorMessage($"分析程序集失败: {Path.GetFileName(dllFile)}", ex);
                        failCount++;
                    }
                }

                _logger.InfoMessage($"DLL 分析完成: 成功 {successCount} 个，失败 {failCount} 个");
            }

            // ========== 第四步：统一返回全量数据 ==========
            // 无论是否找到 DLL，都返回完整的类型字段映射（Core + Mod）
            var allTypeFields = _reflectionAnalyzer.GetAllTypeFields();

            if (allTypeFields.Count > 0)
            {
                int totalFields = allTypeFields.Sum(x => x.Value.Count);
                _logger.InfoMessage($"返回 {allTypeFields.Count} 个可翻译类型，共 {totalFields} 个字段");
                return allTypeFields;
            }

            _logger.WarningMessage("未找到任何可翻译的类型");
            return null;
        }
        catch (Exception ex)
        {
            _logger.ErrorMessage("扫描 Mod 程序集时出错", ex);
            return null;
        }
    }

}
