using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RimTransAI.Services.Scanning;

public sealed class GameLoadOrderPlanner
{
    private static readonly Regex VersionDirRegex = new(@"^\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex VersionTokenRegex = new(@"\d+(\.\d+){1,3}", RegexOptions.Compiled);

    private readonly record struct LoadFolderRule(
        string RelativePath,
        string[] RequiredAnyOfPackageIds,
        string[] RequiredAllOfPackageIds,
        string[] DisallowedAnyOfPackageIds);

    private readonly ILogger<GameLoadOrderPlanner> _logger;

    public GameLoadOrderPlanner(ILogger<GameLoadOrderPlanner>? logger = null)
    {
        _logger = logger ?? NullLogger<GameLoadOrderPlanner>.Instance;
    }

    public bool LastPlanUsedFallbackDueToError { get; private set; }

    /// <summary>
    /// 1.7 引擎只会启用 LoadFolders.xml 中的一个版本节点（完整版本串精确匹配 → 不大于当前版本的最大版本 → default），
    /// 这里是本次规划实际启用的节点键（已小写、去掉前导 v）；未使用 LoadFolders.xml 或回退到目录扫描时为空字符串。
    /// </summary>
    public string LastSelectedVersionKey { get; private set; } = string.Empty;

    public IReadOnlyList<LoadFolderPlanEntry> Plan(ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        LastPlanUsedFallbackDueToError = false;
        LastSelectedVersionKey = string.Empty;
        if (!Directory.Exists(context.ModRootPath))
        {
            return [];
        }

        if (TryPlanFromLoadFolders(context, out var loadFoldersPlan))
        {
            return loadFoldersPlan;
        }

        return PlanFallback(context);
    }

    private bool TryPlanFromLoadFolders(ScanContext context, out List<LoadFolderPlanEntry> plan)
    {
        plan = [];

        var loadFoldersPath = Path.Combine(context.ModRootPath, "LoadFolders.xml");
        if (!File.Exists(loadFoldersPath))
        {
            return false;
        }

        var foldersByVersion = ParseLoadFoldersByVersion(loadFoldersPath);
        if (foldersByVersion.Count == 0)
        {
            return false;
        }

        // 与 ModContentPack.InitLoadFolders 一致：只有一个版本节点会被游戏启用。
        var versionKey = SelectVersionKey(foldersByVersion.Keys, context.CurrentGameVersion);
        if (versionKey == null)
        {
            _logger.InfoMessage(
                $"LoadFolders.xml 中没有适用于当前游戏版本（{context.CurrentGameVersion}）的节点，已回退到目录扫描：{loadFoldersPath}");
            return false;
        }

        var entries = BuildEntriesFromRules(
            context.ModRootPath,
            foldersByVersion[versionKey],
            context.ActivePackageIds);

        if (entries.Count == 0)
        {
            // 游戏在这种情况下会加载不到任何目录；为了让用户仍能翻译（例如节点目录还没建好），
            // 这里回退到目录扫描并明确告警。
            _logger.LogUserWarning(
                "LoadFolders.xml 选中的版本节点「{VersionKey}」下没有任何可用目录，已回退到目录扫描：{ModRootPath}",
                versionKey,
                context.ModRootPath);
            return false;
        }

        LastSelectedVersionKey = versionKey;
        plan = entries;
        return true;
    }

    private Dictionary<string, List<LoadFolderRule>> ParseLoadFoldersByVersion(string loadFoldersPath)
    {
        var result = new Dictionary<string, List<LoadFolderRule>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = XDocument.Load(loadFoldersPath);
            if (doc.Root == null)
            {
                return result;
            }

            foreach (var versionElement in doc.Root.Elements())
            {
                var versionKey = NormalizeVersionKey(versionElement.Name.LocalName);
                if (!result.TryGetValue(versionKey, out var rules))
                {
                    rules = [];
                    result[versionKey] = rules;
                }

                foreach (var folderElement in versionElement.Elements())
                {
                    var folderPath = folderElement.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(folderPath))
                    {
                        continue;
                    }

                    if (folderPath is "/" or "\\")
                    {
                        folderPath = string.Empty;
                    }

                    rules.Add(new LoadFolderRule(
                        folderPath,
                        SplitPackageIds(folderElement.Attribute("IfModActive")?.Value),
                        SplitPackageIds(folderElement.Attribute("IfModActiveAll")?.Value),
                        SplitPackageIds(folderElement.Attribute("IfModNotActive")?.Value)));
                }
            }
        }
        catch (Exception ex)
        {
            LastPlanUsedFallbackDueToError = true;
            _logger.LogUserWarning(
                ex,
                "LoadFolders.xml 解析失败，已回退到目录扫描：{LoadFoldersPath}",
                loadFoldersPath);
            return [];
        }

        return result;
    }

    private static List<LoadFolderPlanEntry> BuildEntriesFromRules(
        string modRootPath,
        IReadOnlyList<LoadFolderRule> rules,
        IReadOnlyCollection<string> activePackageIds)
    {
        var result = new List<LoadFolderPlanEntry>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedActive = BuildNormalizedActivePackageSet(activePackageIds);
        var order = 0;

        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var rule = rules[i];
            if (!ShouldLoad(rule, normalizedActive))
            {
                continue;
            }

            var relativePath = NormalizeRelativePath(rule.RelativePath);
            var isRoot = string.IsNullOrEmpty(relativePath);
            var fullPath = isRoot ? modRootPath : Path.Combine(modRootPath, relativePath);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            fullPath = Path.GetFullPath(fullPath);
            if (!seenFolders.Add(fullPath))
            {
                continue;
            }

            result.Add(new LoadFolderPlanEntry(
                fullPath,
                isRoot ? "." : relativePath,
                InferVersionFromRelativePath(relativePath),
                order++));
        }

        return result;
    }

    private static List<LoadFolderPlanEntry> PlanFallback(ScanContext context)
    {
        var result = new List<LoadFolderPlanEntry>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        void TryAdd(string relativePath)
        {
            var normalized = NormalizeRelativePath(relativePath);
            var isRoot = string.IsNullOrEmpty(normalized);
            var path = isRoot ? context.ModRootPath : Path.Combine(context.ModRootPath, normalized);
            if (!Directory.Exists(path))
            {
                return;
            }

            path = Path.GetFullPath(path);
            if (!seenFolders.Add(path))
            {
                return;
            }

            result.Add(new LoadFolderPlanEntry(
                path,
                isRoot ? "." : normalized,
                InferVersionFromRelativePath(normalized),
                order++));
        }

        var versionDirs = Directory
            .EnumerateDirectories(context.ModRootPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && VersionDirRegex.IsMatch(name))
            .Select(name => (Raw: name!, Parsed: ParseVersionSafe(name!)))
            .Where(x => x.Parsed != null)
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Raw)
            .ToList();

        foreach (var versionDir in versionDirs)
        {
            TryAdd(versionDir);
        }

        TryAdd("Common");
        TryAdd(string.Empty);

        return result;
    }

    private static bool ShouldLoad(LoadFolderRule rule, HashSet<string> activePackageIds)
    {
        // IfModNotActive：仅当被排除的 Mod 确定处于激活状态时跳过。
        if (rule.DisallowedAnyOfPackageIds.Length > 0 &&
            rule.DisallowedAnyOfPackageIds.Any(activePackageIds.Contains))
        {
            return false;
        }

        // IfModActive / IfModActiveAll：采取“失败即加载”。
        // 这里的激活 Mod 集合只是尽力猜测（Core + 自身 + 声明的依赖），
        // 缺少某个包 id 并不能证明该 Mod 未激活；为保证翻译提取的完整性，
        // 条件目录默认加载，避免漏掉可选的集成内容。
        return true;
    }

    private static HashSet<string> BuildNormalizedActivePackageSet(IEnumerable<string> packageIds)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in packageIds)
        {
            var normalized = NormalizePackageId(packageId);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static string NormalizePackageId(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return string.Empty;
        }

        var normalized = packageId.Trim().ToLowerInvariant();
        return normalized.EndsWith("_steam", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^"_steam".Length]
            : normalized;
    }

    private static string[] SplitPackageIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePackageId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeVersionKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.StartsWith('v'))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    private static Version? ParseVersionSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeVersionKey(value);
        var match = VersionTokenRegex.Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var version) ? version : null;
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

    /// <summary>
    /// 复刻 ModContentPack.InitLoadFolders 的版本节点选择：
    /// ① 精确匹配 VersionControl.CurrentVersionString（形如 1.7.4990，只有完整版本串才会命中）；
    /// ② 取不大于当前版本的版本节点中字符串降序的最大者（引擎用 orderby x descending）；
    /// ③ 再否则取 default；④ 都不存在则交由目录扫描回退。
    /// </summary>
    private static string? SelectVersionKey(IEnumerable<string> definedVersions, string? currentGameVersion)
    {
        var versions = definedVersions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (versions.Count == 0)
        {
            return null;
        }

        var currentVersionKey = NormalizeVersionKey(currentGameVersion);
        if (!string.IsNullOrEmpty(currentVersionKey) && currentVersionKey.Split('.').Length >= 3)
        {
            var exact = versions.FirstOrDefault(x =>
                string.Equals(NormalizeVersionKey(x), currentVersionKey, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }

        var currentVersion = ParseVersionSafe(currentGameVersion);
        if (currentVersion != null)
        {
            var fallback = versions
                .Where(x => !string.Equals(x, "default", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Contains('.'))
                .Where(x => ParseVersionSafe(x) is { } parsed && parsed <= currentVersion)
                .OrderByDescending(x => x, StringComparer.Ordinal)
                .FirstOrDefault();
            if (fallback != null)
            {
                return fallback;
            }
        }

        return versions.FirstOrDefault(x =>
            string.Equals(x, "default", StringComparison.OrdinalIgnoreCase));
    }
}
