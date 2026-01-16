using System.Xml.Linq;

namespace RimTransAI.Tests.Helpers;

/// <summary>
/// XML 测试辅助类
/// </summary>
public static class XmlTestHelper
{
    /// <summary>
    /// 从字符串创建 XElement
    /// </summary>
    public static XElement CreateElement(string xml)
    {
        return XElement.Parse(xml);
    }

    /// <summary>
    /// 创建标准的 ThingDef 元素
    /// </summary>
    public static XElement CreateThingDef(string defName, string? label = null, string? description = null)
    {
        var element = new XElement("ThingDef");
        element.Add(new XElement("defName", defName));

        if (label != null)
            element.Add(new XElement("label", label));

        if (description != null)
            element.Add(new XElement("description", description));

        return element;
    }

    /// <summary>
    /// 创建带 Class 属性的 Def 元素
    /// </summary>
    public static XElement CreateDefWithClass(string className, string defName, string? label = null)
    {
        var element = new XElement("Def", new XAttribute("Class", className));
        element.Add(new XElement("defName", defName));

        if (label != null)
            element.Add(new XElement("label", label));

        return element;
    }
}
