using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public sealed class DefFieldExtractionEngine
{
    private static readonly HashSet<string> BlacklistSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "defname", "def",
        "texpath", "texture", "texturepath", "iconpath", "shader",
        "sound", "sounddef", "soundcast", "soundhit", "soundmelee",
        "tag", "tags", "class", "workerclass", "driverclass", "linktype", "drawertype",
        "pathcost", "altitudelayer", "graphicdata",
        "path", "file", "icon", "graphic",
        "setting", "config", "worker", "link", "curve",
        "size", "color", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset",
        "key"
    };

    private static readonly string[] BlacklistKeywords =
    {
        "defname",
        "path", "texpath", "texture", "iconpath", "shader",
        "sound",
        "tag", "class", "worker", "driver", "drawer", "link",
        "pathcost", "altitudelayer", "graphic",
        "file", "icon", "graphic", "setting", "config", "link", "curve",
        "size", "color", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset"
    };

    private static readonly HashSet<string> TechnicalListSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "prerequisites", "researchPrerequisites", "hiddenPrerequisites",
        "requiredResearchFacilities", "requiredMemeList",
        "thingCategories", "stuffCategories", "tradeTags", "weaponTags", "apparelTags", "destroyOnDrop",
        "categories", "filter", "fixedIngredientFilter", "defaultIngredientFilter", "ingredients", "products",
        "specialProducts", "backstoryFiltersOverride",
        "recipeUsers", "recipes", "importRecipesFrom", "excludeOres",
        "workTypes", "roleTags",
        "appliedOnFixedBodyParts", "groups", "hediffGivers",
        "requiredCapacities", "affordances", "capabilities", "statBases", "equippedStatOffsets",
        "capacities",
        "inspectorTabs", "compClass", "comps", "modExtensions"
    };

    private static readonly HashSet<string> WhitelistSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "label", "description",
        "jobstring", "verb", "labelnoun", "pawnlabel",
        "reportstring", "baseinspectline", "deathmessage",
        "text", "lettertext", "letterlabel",
        "rulesstrings", "slateref",
        "questnamerules", "questdescriptionrules",
        "labelshort", "gerund", "message", "labelkey"
    };

    private static readonly string[] SmartSuffixes =
    {
        "label",
        "text",
        "desc",
        "message",
        "string"
    };

    private readonly DefsSourceParser _defsSourceParser;
    private readonly DefPathBuilder _defPathBuilder;

    public DefFieldExtractionEngine(
        DefsSourceParser? defsSourceParser = null,
        DefPathBuilder? defPathBuilder = null)
    {
        _defsSourceParser = defsSourceParser ?? new DefsSourceParser();
        _defPathBuilder = defPathBuilder ?? new DefPathBuilder();
    }

    public List<TranslationItem> Extract(
        ScanContext context,
        XmlSourceCollection sources,
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reflectionMap);

        var results = new List<TranslationItem>();
        var shortNameMap = BuildShortNameMap(reflectionMap);

        ExtractDefs(_defsSourceParser, _defPathBuilder, sources.DefFiles, reflectionMap, shortNameMap, results);
        ExtractKeyed(sources.KeyedFiles, results);

        return results;
    }

    private static void ExtractDefs(
        DefsSourceParser defsSourceParser,
        DefPathBuilder defPathBuilder,
        IReadOnlyList<XmlSourceFile> defFiles,
        Dictionary<string, HashSet<string>> reflectionMap,
        Dictionary<string, List<string>> shortNameMap,
        List<TranslationItem> output)
    {
        var upsert = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in defFiles.OrderBy(x => x.Order))
        {
            try
            {
                var parseResult = defsSourceParser.Parse(source.FullPath);
                if (!string.IsNullOrWhiteSpace(parseResult.ErrorMessage))
                {
                    Logger.Warning($"Defs XML 解析失败 {source.FullPath}: {parseResult.ErrorMessage}");
                    continue;
                }

                if (!parseResult.IsValidDefsRoot)
                {
                    continue;
                }

                if (parseResult.HitTraversalLimit)
                {
                    Logger.Warning($"Defs 文件触发结构遍历保护，已截断: {source.FullPath}");
                }

                foreach (var definition in parseResult.Definitions.OrderBy(x => x.Order))
                {
                    if (string.IsNullOrWhiteSpace(definition.DefName))
                    {
                        continue;
                    }

                    if (IsAbstractDef(definition.Element))
                    {
                        continue;
                    }

                    ExtractDefDefinition(
                        definition,
                        source,
                        defPathBuilder,
                        reflectionMap,
                        shortNameMap,
                        output,
                        upsert);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Defs XML 格式错误 {source.FullPath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Defs 文件出错: {source.FullPath}", ex);
            }
        }
    }

    private static void ExtractDefDefinition(
        ParsedDefEntry definition,
        XmlSourceFile source,
        DefPathBuilder defPathBuilder,
        Dictionary<string, HashSet<string>> reflectionMap,
        Dictionary<string, List<string>> shortNameMap,
        List<TranslationItem> output,
        Dictionary<string, int> upsert)
    {
        var extractedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TraverseNodes(
            definition.Nodes,
            definition.DefType,
            allowListExtraction: false,
            parentContainerTag: string.Empty);

        void TraverseNodes(
            IReadOnlyList<ParsedDefNode> nodes,
            string currentTypeName,
            bool allowListExtraction,
            string parentContainerTag)
        {
            var translatableFields = ResolveTypeFields(currentTypeName, reflectionMap, shortNameMap);
            var hasReflectionData = translatableFields != null && translatableFields.Count > 0;

            foreach (var node in nodes)
            {
                var tagName = node.Name;
                if (tagName.Equals("defName", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (tagName.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsBlacklisted(tagName))
                {
                    continue;
                }

                var shouldExtract = false;
                if (!tagName.Equals("li", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsPathLikeContent(node.Value))
                    {
                        if (IsWhitelistedField(tagName))
                        {
                            if (!IsSafeTextList(tagName))
                            {
                                shouldExtract = true;
                            }
                        }
                        else if (IsSmartSuffixMatch(tagName))
                        {
                            shouldExtract = true;
                        }
                        else if (hasReflectionData &&
                                 translatableFields != null &&
                                 ContainsIgnoreCase(translatableFields, tagName) &&
                                 !node.HasChildren)
                        {
                            shouldExtract = true;
                        }
                    }
                }
                else if (!node.HasChildren && allowListExtraction)
                {
                    if (!TechnicalListSet.Contains(parentContainerTag) &&
                        !IsPathLikeContent(node.Value))
                    {
                        shouldExtract = true;
                    }
                }

                if (shouldExtract)
                {
                    var value = (node.Value ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var key = defPathBuilder.BuildKey(definition.DefName, node.PathSegments);
                        var normalizedKey = defPathBuilder.NormalizeKey(key);
                        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
                            extractedKeys.Add(normalizedKey))
                        {
                            var item = new TranslationItem
                            {
                                Key = normalizedKey,
                                DefType = definition.DefType,
                                OriginalText = value,
                                TranslatedText = string.Empty,
                                Status = "未翻译",
                                FilePath = source.FullPath,
                                Version = source.Version
                            };

                            var dedupeKey = $"defs|{item.DefType}|{item.Version}|{item.Key}";
                            Upsert(output, upsert, dedupeKey, item);
                        }
                    }
                }

                if (!node.HasChildren)
                {
                    continue;
                }

                var nextAllowListExtraction = hasReflectionData && translatableFields != null
                    ? ContainsIgnoreCase(translatableFields, tagName)
                    : IsSafeTextList(tagName);

                var childTypeName = !string.IsNullOrWhiteSpace(node.ClassName)
                    ? node.ClassName
                    : tagName;

                TraverseNodes(
                    node.Children,
                    childTypeName,
                    nextAllowListExtraction,
                    tagName);
            }
        }
    }

    private static bool IsAbstractDef(XElement defElement)
    {
        var abstractAttr = defElement.Attribute("Abstract");
        return abstractAttr != null &&
               abstractAttr.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<string>> BuildShortNameMap(
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullClassName in reflectionMap.Keys)
        {
            var lastDot = fullClassName.LastIndexOf('.');
            var shortName = lastDot >= 0
                ? fullClassName[(lastDot + 1)..]
                : fullClassName;

            if (!result.TryGetValue(shortName, out var list))
            {
                list = [];
                result[shortName] = list;
            }

            list.Add(fullClassName);
        }

        return result;
    }

    private static HashSet<string>? ResolveTypeFields(
        string typeName,
        Dictionary<string, HashSet<string>> reflectionMap,
        Dictionary<string, List<string>> shortNameMap)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        if (reflectionMap.TryGetValue(typeName, out var fields))
        {
            return fields;
        }

        if (!shortNameMap.TryGetValue(typeName, out var fullNames) || fullNames.Count == 0)
        {
            return null;
        }

        var fullName = fullNames[0];
        return reflectionMap.TryGetValue(fullName, out fields) ? fields : null;
    }

    private static bool ContainsIgnoreCase(HashSet<string> set, string value)
    {
        if (set.Contains(value))
        {
            return true;
        }

        foreach (var item in set)
        {
            if (item.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlacklisted(string name)
    {
        if (BlacklistSet.Contains(name))
        {
            return true;
        }

        var span = name.AsSpan();
        foreach (var keyword in BlacklistKeywords)
        {
            if (span.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathLikeContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Contains('/') || value.Contains('\\'))
        {
            return true;
        }

        var lowerValue = value.ToLowerInvariant();
        return lowerValue.EndsWith(".png", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".jpg", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".jpeg", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".gif", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".wav", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".mp3", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".xml", StringComparison.Ordinal) ||
               lowerValue.EndsWith(".txt", StringComparison.Ordinal);
    }

    private static bool IsWhitelistedField(string name)
    {
        return WhitelistSet.Contains(name);
    }

    private static bool IsSmartSuffixMatch(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var lowerName = name.ToLowerInvariant();
        foreach (var suffix in SmartSuffixes)
        {
            if (lowerName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeTextList(string name)
    {
        return name.Equals("rulesstrings", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("descriptions", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("labels", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("messages", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("helptexts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("baseinspectline", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractKeyed(
        IReadOnlyList<XmlSourceFile> keyedFiles,
        List<TranslationItem> output)
    {
        var upsert = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in keyedFiles.OrderBy(x => x.Order))
        {
            try
            {
                var doc = XDocument.Load(source.FullPath);
                if (doc.Root == null)
                {
                    continue;
                }

                var seenKeysInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in doc.Root.Elements())
                {
                    if (element.HasElements)
                    {
                        continue;
                    }

                    var key = element.Name.LocalName;
                    if (!seenKeysInFile.Add(key))
                    {
                        Logger.Warning($"Keyed 文件内重复 Key，已跳过后续项: {key} ({source.FullPath})");
                        continue;
                    }

                    var value = (element.Value ?? string.Empty).Replace("\\n", "\n").Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (value.Equals("TODO", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var item = new TranslationItem
                    {
                        Key = key,
                        DefType = "Keyed",
                        OriginalText = value,
                        TranslatedText = string.Empty,
                        Status = "未翻译",
                        FilePath = source.FullPath,
                        Version = source.Version
                    };

                    var dedupeKey = $"keyed|{item.Key}";
                    Upsert(output, upsert, dedupeKey, item);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Keyed XML 格式错误 {source.FullPath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Keyed 文件出错: {source.FullPath}", ex);
            }
        }
    }

    private static void Upsert(
        List<TranslationItem> target,
        Dictionary<string, int> indexMap,
        string key,
        TranslationItem value)
    {
        if (indexMap.TryGetValue(key, out var existingIndex))
        {
            target[existingIndex] = value;
            return;
        }

        indexMap[key] = target.Count;
        target.Add(value);
    }
}
