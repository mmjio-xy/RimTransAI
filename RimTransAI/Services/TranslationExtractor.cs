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

    /// <summary>
    /// 【第一层：黑名单拦截】绝对黑名单
    /// 这些是程序逻辑字段，翻译了会导致报错或找不到贴图，必须排除
    /// </summary>
    private static readonly HashSet<string> BlacklistSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 身份标识
        "defname", "def",

        // 资源路径
        "texpath", "texture", "texturepath", "iconpath", "shader",

        // 音频资源
        "sound", "sounddef", "soundcast", "soundhit", "soundmelee",

        // 程序逻辑
        "tag", "tags", "class", "workerclass", "driverclass", "linktype", "drawertype",

        // 数据数值
        "pathcost", "altitudelayer", "graphicdata",

        // 其他技术字段
        "path", "file", "icon", "graphic",
        "setting", "config", "worker", "link", "curve",
        "size", "color", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset", "labelshort",

        // 【新增】组件键名
        "key"
    };

    // 性能优化：预编译包含关键词（用于 Contains 检查）
    private static readonly string[] BlacklistKeywords =
    {
        // 身份标识
        "defname", "def",

        // 资源路径
        "path", "texpath", "texture", "iconpath", "shader",

        // 音频资源
        "sound",

        // 程序逻辑
        "tag", "class", "worker", "driver", "drawer", "link",

        // 数据数值
        "pathcost", "altitudelayer", "graphic",

        // 其他技术字段
        "file", "icon", "graphic", "setting", "config", "link", "curve",
        "size", "color", "mask", "effect", "cost", "stat",
        "tex", "coordinates", "offset"
    };

    /// <summary>
    /// 【列表黑名单】技术性列表（其子项 li 绝对不能翻译）
    /// 这些列表包含的是 DefName 引用、Type 类名或枚举，而非显示文本
    /// </summary>
    private static readonly HashSet<string> TechnicalListSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 1. 核心系统与前置
        "prerequisites", "researchPrerequisites", "hiddenPrerequisites",
        "requiredResearchFacilities", // 【新增】研究前置建筑
        "requiredMemeList", // 【新增】文化形态需求

        // 2. 物品分类、标签与过滤器
        "thingCategories", "stuffCategories", "tradeTags", "weaponTags", "apparelTags", "destroyOnDrop",
        "categories", // 【新增】核心过滤器，引发了大量报错
        "filter", "fixedIngredientFilter", "defaultIngredientFilter", "ingredients", "products",
        "specialProducts", // 【新增】特殊产品类型（枚举）
        "backstoryFiltersOverride", // 【新增】背景故事过滤器

        // 3. 配方与生产配置
        "recipeUsers", "recipes",
        "importRecipesFrom", // 【新增】PRF 特有：从其他工作台导入配方
        "excludeOres", // 【新增】PRF 特有：矿机排除列表

        // 4. 工作与行为
        "workTypes", // 【新增】无人机工作类型
        "roleTags", // 【新增】角色标签

        // 5. 身体与健康
        "appliedOnFixedBodyParts", "groups", "hediffGivers",

        // 6. 能力与属性
        "requiredCapacities", "affordances", "capabilities", "statBases", "equippedStatOffsets",
        "capacities", // 工具能力引用

        // 7. UI与程序引用
        "inspectorTabs", "compClass", "comps", "modExtensions",
    };

    /// <summary>
    /// 【第三层：白名单放行】绝对白名单
    /// RimWorld 官方定义的确切文本字段，必须精确匹配
    /// </summary>
    private static readonly HashSet<string> WhitelistSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 基础信息
        "label", "description",

        // UI与交互
        "jobstring", "verb", "labelnoun", "pawnlabel",

        // 消息与日志
        "reportstring", "baseinspectline", "deathmessage",

        // 特定结构
        "text", "lettertext", "letterlabel",

        // 名称生成器
        "rulesstrings", "slateref",

        // 任务系统
        "questnamerules", "questdescriptionrules",

        // 其他（兼容现有逻辑）
        "labelshort", "gerund", "message", "labelkey"
    };

    /// <summary>
    /// 【第四层：启发式模糊匹配】智能后缀列表
    /// 只要标签名以这些词结尾（EndWith），就视为可翻译文本
    /// </summary>
    private static readonly string[] SmartSuffixes =
    {
        "label", // 例如 gerundLabel, skillLabel
        "text", // 例如 successText, failureText, introText
        "desc", // 例如 effectDesc, statDesc
        "message", // 例如 arrivalMessage
        "string" // 例如 formattedString
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
            // 跳过 Abstract="True" 的 Def
            // 抽象 Def 通常作为模板存在，游戏不直接加载其 DefInjected 翻译，提取会导致 "Found no match" 错误
            var abstractAttr = defElement.Attribute("Abstract");
            if (abstractAttr != null && abstractAttr.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 获取 DefType (从元素名或 Class 属性)
            var defTypeName = defElement.Name.LocalName;
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
    private void ExtractFromDef(XElement element, string currentTypeName, string defName, string defType,
        string parentPath, string sourceFile, string version, List<TranslationUnit> results,
        bool allowListExtraction)
    {
        // 跳过 Abstract="True" 的 Def
        // 抽象 Def 通常作为模板存在，游戏不直接加载其 DefInjected 翻译
        // 使用 Trim() 增强鲁棒性，防止 XML 属性值包含空格 (如 "true ") 导致判断失效
        var abstractAttr = element.Attribute("Abstract");
        if (abstractAttr != null && abstractAttr.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 1. 确定类型名
        var localTypeName = currentTypeName;
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
        var hasReflectionData = translatableFields != null && translatableFields.Count > 0;

        var children = element.Elements().ToList();
        var extractedKeys = new HashSet<string>();
        var liIndex = 0; // li 索引计数器

        foreach (var child in children)
        {
            var tagName = child.Name.LocalName;
            if (tagName == "defName") continue;

            // 【新增】过滤组件中的 "key" 字段 (程序配置键，非文本)
            if (tagName.Equals("key", StringComparison.OrdinalIgnoreCase)) continue;

            // --- 第一层：黑名单强制检查 ---
            if (IsBlacklisted(tagName)) continue;

            // --- 强制索引生成逻辑 ---
            string currentSegment;
            if (tagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                currentSegment = liIndex.ToString();
                liIndex++;
            }
            else
            {
                currentSegment = tagName;
            }

            // --- 路径拼接 ---
            var fullPath = string.IsNullOrEmpty(parentPath) ? currentSegment : $"{parentPath}.{currentSegment}";

            var shouldExtract = false;
            var reason = "";

            // --- 场景 A: 普通字段 ---
            if (!tagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                // 第二层：内容特征防御
                if (IsPathLikeContent(child.Value)) continue;

                // 第三层：白名单放行
                if (IsWhitelistedField(tagName))
                {
                    // 【修复】如果它是已知的文本列表容器（如 rulesStrings），不要提取容器本身
                    // 否则会生成一条包含所有子节点文本的垃圾 Key
                    if (!IsSafeTextList(tagName))
                    {
                        shouldExtract = true;
                        reason = "Whitelist";
                    }
                }
                // 第四层：启发式模糊匹配
                else if (IsSmartSuffixMatch(tagName))
                {
                    shouldExtract = true;
                    reason = "SmartSuffix";
                }
                // 反射确认 (仅当无子元素时，防止提取复杂对象容器)
                else if (hasReflectionData && translatableFields!.Contains(tagName) && !child.HasElements)
                {
                    shouldExtract = true;
                    reason = "Reflect_Field";
                }
            }
            // --- 场景 B: 列表项 (li) ---
            else if (!child.HasElements)
            {
                // 【关键】检查父级标签是否在技术列表黑名单中
                // 解决 "Translated non-System.String list item" 错误
                // 必须检查 element.Name (即列表容器的名字)
                if (TechnicalListSet.Contains(element.Name.LocalName))
                {
                    shouldExtract = false;
                }
                else
                {
                    // 内容特征防御
                    if (!IsPathLikeContent(child.Value))
                    {
                        shouldExtract = true;
                        reason = "List_Item";
                    }
                }
            }

            // 执行提取
            if (shouldExtract && !string.IsNullOrWhiteSpace(child.Value))
            {
                if (!string.IsNullOrEmpty(defName))
                {
                    var key = $"{defName}.{fullPath}";
                    // 防止重复 Key (简单的去重机制)
                    if (extractedKeys.Add(key))
                    {
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
            if (!child.HasElements) continue;

            // 决定下一层是否允许提取 li
            var nextAllowListExtraction = false;

            if (hasReflectionData)
            {
                // 反射模式：字段在白名单中
                if (translatableFields!.Contains(tagName)) nextAllowListExtraction = true;
            }
            else
            {
                // 无反射模式：仅允许已知的安全文本列表 (如 rulesStrings)
                if (IsSafeTextList(tagName)) nextAllowListExtraction = true;
            }

            var childTypeName = InferChildType(tagName, child);
            // 【强制传递 fullPath】确保路径连续性
            ExtractFromDef(child, childTypeName, defName, defType, fullPath, sourceFile, version, results,
                nextAllowListExtraction);
        }
    }

    // --- 辅助方法 ---

    /// <summary>
    /// [性能优化] 强制黑名单：这些字段绝对不翻译
    /// 使用预构建的哈希集合，查找速度从 O(n*m) 提升到 O(1)
    /// </summary>
    private static bool IsBlacklisted(string name)
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
    /// 【第二层：内容特征防御】检查内容是否像文件路径
    /// 防止某些 Modder 把图片路径命名为 <iconLabel> 从而骗过其他层
    /// </summary>
    private static bool IsPathLikeContent(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        // 检查是否包含路径分隔符
        if (value.Contains('/') || value.Contains('\\')) return true;

        // 检查是否以常见文件扩展名结尾
        var lowerValue = value.ToLower();
        return lowerValue.EndsWith(".png") || lowerValue.EndsWith(".jpg") ||
               lowerValue.EndsWith(".jpeg") || lowerValue.EndsWith(".gif") ||
               lowerValue.EndsWith(".wav") || lowerValue.EndsWith(".mp3") ||
               lowerValue.EndsWith(".xml") || lowerValue.EndsWith(".txt");
    }

    /// <summary>
    /// 【第三层：白名单放行】绝对白名单检查
    /// RimWorld 官方定义的确切文本字段，必须精确匹配
    /// </summary>
    private bool IsWhitelistedField(string name)
    {
        return WhitelistSet.Contains(name);
    }

    /// <summary>
    /// 【第四层：启发式模糊匹配】智能后缀匹配
    /// 检查字段名是否以特定后缀结尾（忽略大小写），用于捕获 Mod 自定义字段
    /// </summary>
    private bool IsSmartSuffixMatch(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        string lowerName = name.ToLower();

        foreach (var suffix in SmartSuffixes)
        {
            if (lowerName.EndsWith(suffix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查字段名是否为安全的文本列表（用于决定是否提取 li 列表项）
    /// </summary>
    private bool IsSafeTextList(string name)
    {
        return name.Equals("rulesstrings", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("descriptions", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("labels", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("messages", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("helptexts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("baseinspectline", StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string>? ResolveTypeFields(string typeName)
    {
        if (_reflectionMap.TryGetValue(typeName, out var fields)) return fields;
        if (!_shortNameMap.TryGetValue(typeName, out var fulls) || fulls.Count <= 0) return null;
        return _reflectionMap.TryGetValue(fulls[0], out fields) ? fields : null;
    }

    private string InferChildType(string tagName, XElement element)
    {
        var classAttr = element.Attribute("Class");
        return classAttr != null ? classAttr.Value : tagName;
    }
}