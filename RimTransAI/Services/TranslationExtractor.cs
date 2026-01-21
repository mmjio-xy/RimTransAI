using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 核心翻译提取器
/// 负责从 XML 中精准提取需要翻译的文本，并过滤掉路径、ID 和配置项
/// </summary>
public class TranslationExtractor
{
    private readonly Dictionary<string, HashSet<string>> _reflectionMap;
    private readonly Dictionary<string, List<string>> _shortNameMap;

    // 性能优化：预构建黑名单哈希集合，使用 OrdinalIgnoreCase 提升查找速度
    private static readonly HashSet<string> BlacklistSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "path", "file", "texture", "icon", "graphic", "sound", "defname",
        "tag", "setting", "config", "class", "worker", "link", "curve",
        "size", "color", "shader", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset", "labelshort"
    };

    // 性能优化：预编译包含关键词（用于 Contains 检查）
    private static readonly string[] BlacklistKeywords =
    {
        "path", "file", "texture", "icon", "graphic", "sound", "defname",
        "tag", "setting", "config", "class", "worker", "link", "curve",
        "size", "color", "shader", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset"
    };

    public TranslationExtractor(Dictionary<string, HashSet<string>> reflectionMap)
    {
        _reflectionMap = reflectionMap ?? throw new ArgumentNullException(nameof(reflectionMap));
        _shortNameMap = new Dictionary<string, List<string>>();
        BuildShortNameMap();
    }

    private void BuildShortNameMap()
    {
        foreach (var fullClassName in _reflectionMap.Keys)
        {
            // 性能优化：使用 Span<T> 避免字符串分配
            var span = fullClassName.AsSpan();
            int lastDot = span.LastIndexOf('.');
            string shortName = lastDot >= 0 ? span[(lastDot + 1)..].ToString() : fullClassName;

            if (!_shortNameMap.ContainsKey(shortName))
            {
                _shortNameMap[shortName] = new List<string>();
            }
            _shortNameMap[shortName].Add(fullClassName);
        }
    }

    public List<TranslationUnit> Extract(IEnumerable<XElement> validDefs, string sourceFile, string version)
    {
        var results = new List<TranslationUnit>();
        foreach (var defElement in validDefs)
        {
            // 获取 DefType (从元素名或 Class 属性)
            string defTypeName = defElement.Name.LocalName;
            var classAttr = defElement.Attribute("Class");
            if (classAttr != null && !string.IsNullOrWhiteSpace(classAttr.Value))
            {
                defTypeName = classAttr.Value;
            }

            // 根节点（Def）默认不允许提取列表内容
            ExtractFromDef(defElement, "", "", defTypeName, "", sourceFile, version, results, false);
        }
        return results;
    }

    /// <summary>
    /// 递归提取核心逻辑
    /// </summary>
    /// <param name="defType">Def 的类型（用于 DefInjected 路径）</param>
    /// <param name="allowListExtraction">是否允许提取当前节点下的 li 列表项</param>
    private void ExtractFromDef(XElement element, string currentTypeName, string defName, string defType,
        string parentPath, string sourceFile, string version, List<TranslationUnit> results,
        bool allowListExtraction)
    {
        // 1. 确定类型名
        string localTypeName = currentTypeName;
        var classAttr = element.Attribute("Class");
        if (classAttr != null && !string.IsNullOrWhiteSpace(classAttr.Value))
            localTypeName = classAttr.Value;
        else if (string.IsNullOrEmpty(localTypeName))
            localTypeName = element.Name.LocalName;

        // 2. 尝试获取 defName
        if (string.IsNullOrEmpty(defName))
        {
            var defNameNode = element.Element("defName");
            if (defNameNode != null) defName = defNameNode.Value.Trim();
        }

        // 3. 获取反射数据
        var translatableFields = ResolveTypeFields(localTypeName);
        bool hasReflectionData = translatableFields != null && translatableFields.Count > 0;

        var children = element.Elements().ToList();
        var extractedKeys = new HashSet<string>();
        int listIndex = 0;
        bool isList = children.Count > 0 && children.All(e => e.Name.LocalName == "li");

        foreach (var child in children)
        {
            string tagName = child.Name.LocalName;
            if (tagName == "defName") continue;

            // --- 关键修复 1：黑名单强制检查 ---
            // 无论反射怎么说，如果字段名看起来像路径，直接跳过
            if (IsBlacklisted(tagName)) continue;

            string currentSegment = (isList || tagName == "li") ? listIndex++.ToString() : tagName;
            string fullPath = string.IsNullOrEmpty(parentPath) ? currentSegment : $"{parentPath}.{currentSegment}";

            bool shouldExtract = false;
            string reason = "";

            // --- 场景 A: 普通字段 ---
            if (tagName != "li")
            {
                if (hasReflectionData && translatableFields!.Contains(tagName) && !child.HasElements)
                {
                    shouldExtract = true;
                    reason = "Reflect_Field";
                }
                else if (!hasReflectionData && IsStandardTranslationField(tagName) && !child.HasElements)
                {
                    shouldExtract = true;
                    reason = "Heuristic_Field";
                }
            }
            // --- 场景 B: 列表项 (li) ---
            else if (tagName == "li" && !child.HasElements)
            {
                // 只有父级被标记为“允许提取列表”（是文本列表），才提取 li
                if (allowListExtraction)
                {
                    // 额外检查：列表里的内容看起来是不是像路径？
                    if (!IsLikePathOrId(child.Value)) 
                    {
                        shouldExtract = true;
                        reason = "Allowed_List_Item";
                    }
                }
            }

            // 执行提取
            if (shouldExtract && !string.IsNullOrWhiteSpace(child.Value))
            {
                if (!string.IsNullOrEmpty(defName))
                {
                    string key = $"{defName}.{fullPath}";
                    if (!extractedKeys.Contains(key))
                    {
                        extractedKeys.Add(key);
                        results.Add(new TranslationUnit
                        {
                            Key = key,
                            DefType = defType,
                            OriginalText = child.Value.Trim(),
                            SourceFile = sourceFile,
                            Context = $"Type: {localTypeName}, Reason: {reason}",
                            Version = version
                        });
                    }
                }
            }

            // --- 递归处理 ---
            if (child.HasElements)
            {
                // 决定下一层是否允许提取 li
                bool nextAllowListExtraction = false;

                if (hasReflectionData)
                {
                    // 如果反射确认它是可翻译字段（且不是黑名单），则允许下一层提取
                    if (translatableFields!.Contains(tagName)) nextAllowListExtraction = true;
                }
                else
                {
                    // 无反射时，只有白名单列表（如 rulesStrings）才允许提取下一层
                    if (IsSafeTextList(tagName)) nextAllowListExtraction = true;
                }

                string childTypeName = InferChildType(tagName, child);
                ExtractFromDef(child, childTypeName, defName, defType, fullPath, sourceFile, version, results, nextAllowListExtraction);
            }
        }
    }

    // --- 辅助方法 ---

    /// <summary>
    /// [性能优化] 强制黑名单：这些字段绝对不翻译
    /// 使用预构建的哈希集合，查找速度从 O(n*m) 提升到 O(1)
    /// </summary>
    private bool IsBlacklisted(string name)
    {
        // 快速检查：完全匹配黑名单中的关键字（忽略大小写）
        if (BlacklistSet.Contains(name))
        {
            return true;
        }

        // 慢速检查：包含黑名单关键字（用于复合字段名如 "texPath", "costList"）
        // 使用 AsSpan 避免字符串分配
        var nameSpan = name.AsSpan();
        foreach (var keyword in BlacklistKeywords)
        {
            if (nameSpan.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// [新增] 检查内容是否像路径或ID (用于列表项的二次检查)
    /// </summary>
    private bool IsLikePathOrId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        // 包含斜杠通常是路径
        if (value.Contains("/") || value.Contains("\\")) return true;
        // 只有字母数字且没有空格，很可能是 DefName ID，而不是自然语言
        // (当然有些短单词也是，这里只做简单启发)
        // if (!value.Contains(" ") && value.Length > 20) return true; 
        return false;
    }

    private bool IsSafeTextList(string name)
    {
        return name == "rulesStrings" || 
               name == "descriptions" || 
               name == "labels" || 
               name == "messages" || 
               name == "helpTexts" ||
               name == "baseInspectLine";
    }

    private bool IsStandardTranslationField(string name)
    {
        return name == "label" || name == "labelShort" || name == "labelNoun" ||
               name == "description" || name == "jobString" || name == "verb" ||
               name == "gerund" || name == "text" || name == "message" ||
               name == "letterLabel" || name == "letterText" || name == "deathMessage" ||
               name == "labelKey" || name == "reportString";
    }

    private HashSet<string>? ResolveTypeFields(string typeName)
    {
        if (_reflectionMap.TryGetValue(typeName, out var fields)) return fields;
        if (_shortNameMap.TryGetValue(typeName, out var fulls) && fulls.Count > 0)
        {
            if (_reflectionMap.TryGetValue(fulls[0], out fields)) return fields;
        }
        return null;
    }

    private string InferChildType(string tagName, XElement element)
    {
        var classAttr = element.Attribute("Class");
        return classAttr != null ? classAttr.Value : tagName;
    }
}