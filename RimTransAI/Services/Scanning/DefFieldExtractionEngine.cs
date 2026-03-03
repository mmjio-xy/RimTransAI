using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public enum ExtractionConflictPolicy
{
    LastWriteWins,
    FirstWriteWins
}

public sealed class DefFieldExtractionEngine
{
    private readonly DefsSourceParser _defsSourceParser;
    private readonly DefPathBuilder _defPathBuilder;
    private readonly FieldExtractionRuleSet _ruleSet;
    private readonly ExtractionConflictPolicy _conflictPolicy;
    public ExtractionDiagnostics LastDiagnostics { get; private set; } = new();

    public DefFieldExtractionEngine(
        DefsSourceParser? defsSourceParser = null,
        DefPathBuilder? defPathBuilder = null,
        FieldExtractionRuleSet? ruleSet = null,
        ExtractionConflictPolicy conflictPolicy = ExtractionConflictPolicy.LastWriteWins)
    {
        _defsSourceParser = defsSourceParser ?? new DefsSourceParser();
        _defPathBuilder = defPathBuilder ?? new DefPathBuilder();
        _ruleSet = ruleSet ?? FieldExtractionRuleSet.CreateDefault();
        _conflictPolicy = conflictPolicy;
    }

    public List<TranslationItem> Extract(
        ScanContext context,
        XmlSourceCollection sources,
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reflectionMap);

        var normalizedReflectionMap = BuildNormalizedReflectionMap(reflectionMap);
        var shortNameMap = BuildShortNameMap(normalizedReflectionMap);

        var diagnostics = new ExtractionDiagnostics();
        var results = new List<TranslationItem>();
        ExtractDefs(_defsSourceParser, _defPathBuilder, _ruleSet, _conflictPolicy, sources.DefFiles, normalizedReflectionMap, shortNameMap, results, diagnostics);
        ExtractDefInjected(_conflictPolicy, _defPathBuilder, sources.DefInjectedFiles, results, diagnostics);
        ExtractBackstories(_conflictPolicy, _defPathBuilder, sources.BackstoryFiles, results, diagnostics);
        ExtractKeyed(_conflictPolicy, sources.KeyedFiles, results, diagnostics);
        diagnostics.ExtractedItemCount = results.Count;
        LastDiagnostics = diagnostics;
        return results;
    }

    private static void ExtractDefs(
        DefsSourceParser defsSourceParser,
        DefPathBuilder defPathBuilder,
        FieldExtractionRuleSet ruleSet,
        ExtractionConflictPolicy conflictPolicy,
        IReadOnlyList<XmlSourceFile> defFiles,
        Dictionary<string, HashSet<string>> reflectionMap,
        Dictionary<string, List<string>> shortNameMap,
        List<TranslationItem> output,
        ExtractionDiagnostics diagnostics)
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
                    diagnostics.ErrorCount++;
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
                        ruleSet,
                        conflictPolicy,
                        reflectionMap,
                        shortNameMap,
                        output,
                        upsert,
                        diagnostics);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Defs XML 格式错误 {source.FullPath}: {ex.Message}");
                diagnostics.ErrorCount++;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Defs 文件出错: {source.FullPath}", ex);
                diagnostics.ErrorCount++;
            }
        }
    }

    private static void ExtractDefDefinition(
        ParsedDefEntry definition,
        XmlSourceFile source,
        DefPathBuilder defPathBuilder,
        FieldExtractionRuleSet ruleSet,
        ExtractionConflictPolicy conflictPolicy,
        Dictionary<string, HashSet<string>> reflectionMap,
        Dictionary<string, List<string>> shortNameMap,
        List<TranslationItem> output,
        Dictionary<string, int> upsert,
        ExtractionDiagnostics diagnostics)
    {
        var extractedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TraverseNodes(definition.Nodes, definition.DefType, allowListExtraction: false, parentContainerTag: string.Empty);

        void TraverseNodes(
            IReadOnlyList<ParsedDefNode> nodes,
            string currentTypeName,
            bool allowListExtraction,
            string parentContainerTag)
        {
            var translatableFields = ResolveTypeFields(currentTypeName, reflectionMap, shortNameMap);
            var hasReflectionData = translatableFields is { Count: > 0 };

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

                if (ruleSet.IsBlacklisted(tagName))
                {
                    continue;
                }

                var reasonCode = TryResolveReasonCode(
                    node,
                    tagName,
                    parentContainerTag,
                    allowListExtraction,
                    hasReflectionData,
                    translatableFields,
                    ruleSet);

                if (!string.IsNullOrWhiteSpace(reasonCode))
                {
                    var value = (node.Value ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var key = defPathBuilder.BuildKey(definition.DefName, node.PathSegments);
                        var normalizedKey = defPathBuilder.NormalizeKey(key);
                        if (!string.IsNullOrWhiteSpace(normalizedKey) && extractedKeys.Add(normalizedKey))
                        {
                            var item = new TranslationItem
                            {
                                Key = normalizedKey,
                                DefType = definition.DefType,
                                OriginalText = value,
                                TranslatedText = string.Empty,
                                Status = "未翻译",
                                FilePath = source.FullPath,
                                Version = source.Version,
                                ExtractionReasonCode = reasonCode,
                                ExtractionSourceContext = BuildDefsContext(definition, node, currentTypeName)
                            };

                            var dedupeKey = $"defs|{item.DefType}|{item.Version}|{item.Key}";
                            Upsert(output, upsert, dedupeKey, item, conflictPolicy, diagnostics);
                        }
                    }
                }

                if (!node.HasChildren)
                {
                    continue;
                }

                var nextAllowListExtraction = hasReflectionData && translatableFields != null
                    ? translatableFields.Contains(tagName)
                    : ruleSet.IsSafeTextList(tagName);

                var childTypeName = !string.IsNullOrWhiteSpace(node.ClassName)
                    ? node.ClassName
                    : tagName;

                TraverseNodes(node.Children, childTypeName, nextAllowListExtraction, tagName);
            }
        }
    }

    private static string? TryResolveReasonCode(
        ParsedDefNode node,
        string tagName,
        string parentContainerTag,
        bool allowListExtraction,
        bool hasReflectionData,
        HashSet<string>? translatableFields,
        FieldExtractionRuleSet ruleSet)
    {
        if (!tagName.Equals("li", StringComparison.OrdinalIgnoreCase))
        {
            if (ruleSet.IsPathLikeContent(node.Value))
            {
                return null;
            }

            if (ruleSet.IsWhitelistedField(tagName) && !ruleSet.IsSafeTextList(tagName))
            {
                return ExtractionReasonCodes.DefWhitelist;
            }

            if (ruleSet.IsSmartSuffixMatch(tagName))
            {
                return ExtractionReasonCodes.DefSmartSuffix;
            }

            if (hasReflectionData && translatableFields != null && translatableFields.Contains(tagName) && !node.HasChildren)
            {
                return ExtractionReasonCodes.DefReflectionField;
            }

            return null;
        }

        if (!node.HasChildren && allowListExtraction)
        {
            if (!ruleSet.IsTechnicalList(parentContainerTag) && !ruleSet.IsPathLikeContent(node.Value))
            {
                return ExtractionReasonCodes.DefListItem;
            }
        }

        return null;
    }

    private static string BuildDefsContext(
        ParsedDefEntry definition,
        ParsedDefNode node,
        string currentTypeName)
    {
        return $"DefName={definition.DefName};Path={node.Path};Type={currentTypeName};Tag={node.Name}";
    }

    private static void ExtractKeyed(
        ExtractionConflictPolicy conflictPolicy,
        IReadOnlyList<XmlSourceFile> keyedFiles,
        List<TranslationItem> output,
        ExtractionDiagnostics diagnostics)
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
                        diagnostics.ConflictCount++;
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
                        Version = source.Version,
                        ExtractionReasonCode = ExtractionReasonCodes.KeyedLeaf,
                        ExtractionSourceContext = $"Path={source.RelativePath};Key={key};Source=Keyed"
                    };

                    var dedupeKey = $"keyed|{item.Key}";
                    Upsert(output, upsert, dedupeKey, item, conflictPolicy, diagnostics);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Keyed XML 格式错误 {source.FullPath}: {ex.Message}");
                diagnostics.ErrorCount++;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Keyed 文件出错: {source.FullPath}", ex);
                diagnostics.ErrorCount++;
            }
        }
    }

    private static void ExtractDefInjected(
        ExtractionConflictPolicy conflictPolicy,
        DefPathBuilder defPathBuilder,
        IReadOnlyList<XmlSourceFile> defInjectedFiles,
        List<TranslationItem> output,
        ExtractionDiagnostics diagnostics)
    {
        var upsert = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in defInjectedFiles.OrderBy(x => x.Order))
        {
            try
            {
                var doc = XDocument.Load(source.FullPath);
                if (doc.Root == null)
                {
                    continue;
                }

                var defType = ResolveDefInjectedDefType(source);
                foreach (var element in doc.Root.Elements())
                {
                    if (element.Name.LocalName.Equals("rep", StringComparison.OrdinalIgnoreCase))
                    {
                        TryExtractLegacyRepEntry(element, defPathBuilder, source, defType, conflictPolicy, output, upsert, diagnostics);
                        continue;
                    }

                    var key = defPathBuilder.NormalizeKey(element.Name.LocalName);
                    if (string.IsNullOrWhiteSpace(key) || !key.Contains('.', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (key.EndsWith(".slateRef", StringComparison.OrdinalIgnoreCase))
                    {
                        var slateValue = element.HasElements
                            ? string.Concat(element.Nodes())
                            : element.Value;
                        TryAppendDefInjectedLeaf(
                            defPathBuilder,
                            key,
                            slateValue,
                            source,
                            defType,
                            conflictPolicy,
                            output,
                            upsert,
                            diagnostics);
                        continue;
                    }

                    if (!element.HasElements)
                    {
                        TryAppendDefInjectedLeaf(
                            defPathBuilder,
                            key,
                            element.Value,
                            source,
                            defType,
                            conflictPolicy,
                            output,
                            upsert,
                            diagnostics);
                        continue;
                    }

                    var listIndex = 0;
                    foreach (var listItem in element.Descendants().Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                    {
                        var indexedKey = defPathBuilder.NormalizeKey($"{key}.{listIndex}");
                        TryAppendDefInjectedLeaf(
                            defPathBuilder,
                            indexedKey,
                            listItem.Value,
                            source,
                            defType,
                            conflictPolicy,
                            output,
                            upsert,
                            diagnostics,
                            ExtractionReasonCodes.DefInjectedListItem);
                        listIndex++;
                    }
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"DefInjected XML 格式错误 {source.FullPath}: {ex.Message}");
                diagnostics.ErrorCount++;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 DefInjected 文件出错: {source.FullPath}", ex);
                diagnostics.ErrorCount++;
            }
        }
    }

    private static void ExtractBackstories(
        ExtractionConflictPolicy conflictPolicy,
        DefPathBuilder defPathBuilder,
        IReadOnlyList<XmlSourceFile> backstoryFiles,
        List<TranslationItem> output,
        ExtractionDiagnostics diagnostics)
    {
        var upsert = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in backstoryFiles.OrderBy(x => x.Order))
        {
            try
            {
                var doc = XDocument.Load(source.FullPath);
                if (doc.Root == null)
                {
                    continue;
                }

                foreach (var backstoryElement in doc.Root.Elements())
                {
                    var identifier = backstoryElement.Name.LocalName?.Trim();
                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        continue;
                    }

                    TryAppendBackstoryField(
                        defPathBuilder,
                        identifier,
                        "title",
                        backstoryElement.Element("title")?.Value,
                        source,
                        conflictPolicy,
                        output,
                        upsert,
                        diagnostics);
                    TryAppendBackstoryField(
                        defPathBuilder,
                        identifier,
                        "titleFemale",
                        backstoryElement.Element("titleFemale")?.Value,
                        source,
                        conflictPolicy,
                        output,
                        upsert,
                        diagnostics);
                    TryAppendBackstoryField(
                        defPathBuilder,
                        identifier,
                        "titleShort",
                        backstoryElement.Element("titleShort")?.Value,
                        source,
                        conflictPolicy,
                        output,
                        upsert,
                        diagnostics);
                    TryAppendBackstoryField(
                        defPathBuilder,
                        identifier,
                        "titleShortFemale",
                        backstoryElement.Element("titleShortFemale")?.Value,
                        source,
                        conflictPolicy,
                        output,
                        upsert,
                        diagnostics);
                    TryAppendBackstoryField(
                        defPathBuilder,
                        identifier,
                        "description",
                        backstoryElement.Element("desc")?.Value,
                        source,
                        conflictPolicy,
                        output,
                        upsert,
                        diagnostics);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Backstories XML 格式错误 {source.FullPath}: {ex.Message}");
                diagnostics.ErrorCount++;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Backstories 文件出错: {source.FullPath}", ex);
                diagnostics.ErrorCount++;
            }
        }
    }

    private static void TryAppendBackstoryField(
        DefPathBuilder defPathBuilder,
        string identifier,
        string fieldName,
        string? rawValue,
        XmlSourceFile source,
        ExtractionConflictPolicy conflictPolicy,
        List<TranslationItem> output,
        Dictionary<string, int> upsert,
        ExtractionDiagnostics diagnostics)
    {
        var value = (rawValue ?? string.Empty).Replace("\\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Equals("TODO", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedKey = defPathBuilder.NormalizeKey($"{identifier}.{fieldName}");
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        var item = new TranslationItem
        {
            Key = normalizedKey,
            DefType = "BackstoryDef",
            OriginalText = value,
            TranslatedText = string.Empty,
            Status = "未翻译",
            FilePath = source.FullPath,
            Version = source.Version,
            ExtractionReasonCode = ExtractionReasonCodes.BackstoryLegacy,
            ExtractionSourceContext = $"Path={source.RelativePath};Backstory={identifier};Field={fieldName};Source=Backstories"
        };

        var dedupeKey = $"backstories|{item.Key}";
        Upsert(output, upsert, dedupeKey, item, conflictPolicy, diagnostics);
    }

    private static void TryExtractLegacyRepEntry(
        XElement repElement,
        DefPathBuilder defPathBuilder,
        XmlSourceFile source,
        string defType,
        ExtractionConflictPolicy conflictPolicy,
        List<TranslationItem> output,
        Dictionary<string, int> upsert,
        ExtractionDiagnostics diagnostics)
    {
        var pathElement = repElement.Elements()
            .FirstOrDefault(x => x.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase));
        if (pathElement == null)
        {
            return;
        }

        var translationElement = repElement.Elements()
            .FirstOrDefault(x => x.Name.LocalName.Equals("trans", StringComparison.OrdinalIgnoreCase));
        var key = defPathBuilder.NormalizeKey(pathElement.Value);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        TryAppendDefInjectedLeaf(
            defPathBuilder,
            key,
            translationElement?.Value ?? string.Empty,
            source,
            defType,
            conflictPolicy,
            output,
            upsert,
            diagnostics);
    }

    private static void TryAppendDefInjectedLeaf(
        DefPathBuilder defPathBuilder,
        string key,
        string? rawValue,
        XmlSourceFile source,
        string defType,
        ExtractionConflictPolicy conflictPolicy,
        List<TranslationItem> output,
        Dictionary<string, int> upsert,
        ExtractionDiagnostics diagnostics,
        string reasonCode = ExtractionReasonCodes.DefInjectedLeaf)
    {
        var normalizedKey = defPathBuilder.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        var value = (rawValue ?? string.Empty).Replace("\\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Equals("TODO", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var item = new TranslationItem
        {
            Key = normalizedKey,
            DefType = defType,
            OriginalText = value,
            TranslatedText = string.Empty,
            Status = "未翻译",
            FilePath = source.FullPath,
            Version = source.Version,
            ExtractionReasonCode = reasonCode,
            ExtractionSourceContext = $"Path={source.RelativePath};Key={normalizedKey};Source=DefInjected"
        };

        var dedupeKey = $"definjected|{item.DefType}|{item.Key}";
        Upsert(output, upsert, dedupeKey, item, conflictPolicy, diagnostics);
    }

    private static string ResolveDefInjectedDefType(XmlSourceFile source)
    {
        const string prefix = "DefInjected:";
        if (source.SourceKind.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return source.SourceKind[prefix.Length..];
        }

        var segments = source.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("DefInjected", StringComparison.OrdinalIgnoreCase) ||
                segments[i].Equals("DefLinked", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }

        return "Def";
    }

    private static bool IsAbstractDef(XElement defElement)
    {
        var abstractAttr = defElement.Attribute("Abstract");
        return abstractAttr != null && abstractAttr.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, HashSet<string>> BuildNormalizedReflectionMap(
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in reflectionMap)
        {
            var normalizedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in pair.Value)
            {
                if (string.IsNullOrWhiteSpace(field))
                {
                    continue;
                }

                normalizedFields.Add(field.Trim());
                normalizedFields.Add(CleanBackingFieldName(field));
            }

            result[pair.Key] = normalizedFields;
        }

        return result;
    }

    private static string CleanBackingFieldName(string fieldName)
    {
        var start = fieldName.IndexOf('<');
        var end = fieldName.IndexOf('>');
        if (start >= 0 && end > start)
        {
            return fieldName.Substring(start + 1, end - start - 1);
        }

        return fieldName;
    }

    private static Dictionary<string, List<string>> BuildShortNameMap(
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullClassName in reflectionMap.Keys)
        {
            var lastDot = fullClassName.LastIndexOf('.');
            var shortName = lastDot >= 0 ? fullClassName[(lastDot + 1)..] : fullClassName;

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

    private static void Upsert(
        List<TranslationItem> target,
        Dictionary<string, int> indexMap,
        string key,
        TranslationItem value,
        ExtractionConflictPolicy conflictPolicy,
        ExtractionDiagnostics diagnostics)
    {
        if (indexMap.TryGetValue(key, out var existingIndex))
        {
            diagnostics.ConflictCount++;
            if (conflictPolicy == ExtractionConflictPolicy.LastWriteWins)
            {
                target[existingIndex] = value;
            }

            return;
        }

        indexMap[key] = target.Count;
        target.Add(value);
    }
}
