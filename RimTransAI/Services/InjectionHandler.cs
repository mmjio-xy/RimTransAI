using System;
using System.Collections.Generic;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 处理 XML 节点的注入逻辑，基于反射分析结果提取可翻译字段
/// </summary>
public class InjectionHandler
{
    // 存储类名到字段列表的映射
    private readonly Dictionary<string, HashSet<string>> _classFieldsMap;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="classFieldsMap">从 ReflectionAnalyzer 获取的类名到字段列表的映射</param>
    public InjectionHandler(Dictionary<string, HashSet<string>> classFieldsMap)
    {
        _classFieldsMap = classFieldsMap ?? throw new ArgumentNullException(nameof(classFieldsMap));
    }

    /// <summary>
    /// 处理 XML 节点，提取需要注入翻译的项
    /// </summary>
    /// <param name="node">要处理的 XML 节点</param>
    /// <returns>需要注入翻译的项集合</returns>
    public IEnumerable<InjectionItem> ProcessNode(XElement node)
    {
        if (node == null)
        {
            yield break;
        }

        // 1. 检查节点是否有 Class 属性
        var classAttr = node.Attribute("Class");
        if (classAttr == null || string.IsNullOrWhiteSpace(classAttr.Value))
        {
            yield break;
        }

        string className = classAttr.Value;

        // 2. 在字典中查找对应的字段列表
        if (!_classFieldsMap.TryGetValue(className, out var fieldNames))
        {
            yield break;
        }

        // 3. 获取 defName（用于构建完整的 Key）
        var defNameElement = node.Element("defName");
        if (defNameElement == null || string.IsNullOrWhiteSpace(defNameElement.Value))
        {
            yield break;
        }

        string defName = defNameElement.Value.Trim();

        // 4. 遍历字段列表，检查节点是否有同名的子元素
        foreach (var fieldName in fieldNames)
        {
            var fieldElement = node.Element(fieldName);

            // 检查子元素是否存在且有文本内容
            if (fieldElement != null && !string.IsNullOrWhiteSpace(fieldElement.Value))
            {
                yield return new InjectionItem
                {
                    DefName = defName,
                    FieldName = fieldName,
                    OriginalText = fieldElement.Value.Trim()
                };
            }
        }
    }
}
