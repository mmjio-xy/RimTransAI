using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    public IReadOnlyList<LoadFolderPlanEntry> Plan(ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
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

    private static bool TryPlanFromLoadFolders(ScanContext context, out List<LoadFolderPlanEntry> plan)
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

        if (foldersByVersion.TryGetValue(NormalizeVersionKey(context.CurrentGameVersion), out var exactFolders) &&
            exactFolders.Count > 0)
        {
            plan = BuildEntriesFromRules(context.ModRootPath, exactFolders, context.ActivePackageIds);
            return true;
        }

        var fallbackVersion = SelectClosestCompatibleVersion(
            foldersByVersion.Keys,
            context.CurrentGameVersion);
        if (!string.IsNullOrEmpty(fallbackVersion))
        {
            plan = BuildEntriesFromRules(context.ModRootPath, foldersByVersion[fallbackVersion], context.ActivePackageIds);
            return true;
        }

        if (foldersByVersion.TryGetValue("default", out var defaultFolders))
        {
            plan = BuildEntriesFromRules(context.ModRootPath, defaultFolders, context.ActivePackageIds);
            return true;
        }

        return false;
    }

    private static Dictionary<string, List<LoadFolderRule>> ParseLoadFoldersByVersion(string loadFoldersPath)
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
        catch
        {
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

        var currentVersionNoBuild = NormalizeVersionKey(context.CurrentGameVersion);
        var hasExactVersion = !string.IsNullOrEmpty(currentVersionNoBuild) &&
                              Directory.Exists(Path.Combine(context.ModRootPath, currentVersionNoBuild));
        if (hasExactVersion)
        {
            TryAdd(currentVersionNoBuild);
        }
        else
        {
            var parsedVersionDirs = Directory
                .EnumerateDirectories(context.ModRootPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && VersionDirRegex.IsMatch(name))
                .Select(name => (Raw: name!, Parsed: ParseVersionSafe(name!)))
                .Where(x => x.Parsed != null)
                .OrderBy(x => x.Parsed)
                .ToList();

            var currentVersion = ParseVersionSafe(currentVersionNoBuild);
            if (currentVersion == null && parsedVersionDirs.Count > 0)
            {
                currentVersion = parsedVersionDirs[^1].Parsed;
            }

            if (currentVersion != null)
            {
                Version selected = new(0, 0);
                foreach (var item in parsedVersionDirs)
                {
                    var version = item.Parsed!;
                    if ((version > selected || selected > currentVersion) &&
                        (version <= currentVersion || selected.Major == 0))
                    {
                        selected = version;
                    }
                }

                if (selected.Major > 0)
                {
                    TryAdd(selected.ToString());
                }
            }
        }

        TryAdd("Common");
        TryAdd(string.Empty);

        return result;
    }

    private static string SelectClosestCompatibleVersion(IEnumerable<string> definedVersions, string currentGameVersion)
    {
        var current = ParseVersionSafe(currentGameVersion);
        if (current == null)
        {
            return string.Empty;
        }

        var candidates = definedVersions
            .Where(x => !string.Equals(x, "default", StringComparison.OrdinalIgnoreCase) && x.Contains('.'))
            .Select(x => (Raw: x, Parsed: ParseVersionSafe(x)))
            .Where(x =>
            {
                return x.Parsed != null && x.Parsed <= current;
            })
            .OrderByDescending(x => x.Parsed)
            .ToList();

        return candidates.Count > 0 ? candidates[0].Raw : string.Empty;
    }

    private static bool ShouldLoad(LoadFolderRule rule, HashSet<string> activePackageIds)
    {
        var requiredAnyMatched = rule.RequiredAnyOfPackageIds.Length == 0 ||
                                 rule.RequiredAnyOfPackageIds.Any(activePackageIds.Contains);
        if (!requiredAnyMatched)
        {
            return false;
        }

        var requiredAllMatched = rule.RequiredAllOfPackageIds.Length == 0 ||
                                 rule.RequiredAllOfPackageIds.All(activePackageIds.Contains);
        if (!requiredAllMatched)
        {
            return false;
        }

        return rule.DisallowedAnyOfPackageIds.Length == 0 ||
               rule.DisallowedAnyOfPackageIds.All(x => !activePackageIds.Contains(x));
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
}
