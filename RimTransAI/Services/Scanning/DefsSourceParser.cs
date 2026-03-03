using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RimTransAI.Services.Scanning;

public sealed record DefsSourceParserOptions(
    int MaxTraversalDepth = 64,
    int MaxTraversalNodes = 50000);

public sealed record ParsedDefNode(
    string Name,
    string ClassName,
    string Path,
    IReadOnlyList<string> PathSegments,
    string Value,
    int Depth,
    IReadOnlyList<ParsedDefNode> Children)
{
    public bool HasChildren => Children.Count > 0;
}

public sealed record ParsedDefEntry(
    string DefType,
    string DefName,
    XElement Element,
    IReadOnlyList<ParsedDefNode> Nodes,
    int Order);

public sealed class DefsSourceParseResult
{
    public bool IsValidDefsRoot { get; set; }

    public bool HitTraversalLimit { get; set; }

    public string? ErrorMessage { get; set; }

    public List<ParsedDefEntry> Definitions { get; } = [];
}

public sealed class DefsSourceParser
{
    private static readonly Regex StringFormatSymbolsRegex = new("{.*?}", RegexOptions.Compiled);

    private static readonly string HandleAllowedCharacters = "qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM1234567890-_";

    private readonly DefsSourceParserOptions _options;
    private readonly DefPathBuilder _pathBuilder;

    public DefsSourceParser(
        DefsSourceParserOptions? options = null,
        DefPathBuilder? pathBuilder = null)
    {
        _options = options ?? new DefsSourceParserOptions();
        _pathBuilder = pathBuilder ?? new DefPathBuilder();
    }

    public DefsSourceParseResult Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
        }

        try
        {
            var doc = XDocument.Load(filePath, LoadOptions.None);
            if (doc.Root == null ||
                !doc.Root.Name.LocalName.Equals("Defs", StringComparison.OrdinalIgnoreCase))
            {
                return new DefsSourceParseResult
                {
                    IsValidDefsRoot = false
                };
            }

            var result = new DefsSourceParseResult
            {
                IsValidDefsRoot = true
            };

            var order = 0;
            var traversalNodeCount = 0;
            var hitTraversalLimit = false;

            // 保留 XML 文档顺序以贴合游戏加载语义，同时保证结果可复现。
            foreach (var defElement in doc.Root.Elements())
            {
                if (traversalNodeCount >= _options.MaxTraversalNodes)
                {
                    hitTraversalLimit = true;
                    break;
                }

                var defType = ResolveDefType(defElement);
                var defName = ResolveDefName(defElement);
                var nodes = BuildChildren(defElement, 0, ref traversalNodeCount, ref hitTraversalLimit);

                result.Definitions.Add(new ParsedDefEntry(
                    defType,
                    defName,
                    defElement,
                    nodes,
                    order++));

                if (hitTraversalLimit)
                {
                    break;
                }
            }

            result.HitTraversalLimit = hitTraversalLimit;
            return result;
        }
        catch (XmlException ex)
        {
            return new DefsSourceParseResult
            {
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            return new DefsSourceParseResult
            {
                ErrorMessage = ex.Message
            };
        }
    }

    private List<ParsedDefNode> BuildChildren(
        XElement parent,
        int depth,
        ref int traversalNodeCount,
        ref bool hitTraversalLimit)
    {
        var nodes = new List<ParsedDefNode>();
        foreach (var childWithSegment in BuildChildSegments(parent))
        {
            if (hitTraversalLimit)
            {
                break;
            }

            nodes.Add(BuildNode(
                childWithSegment.Element,
                [childWithSegment.Segment],
                depth + 1,
                ref traversalNodeCount,
                ref hitTraversalLimit));
        }

        return nodes;
    }

    private ParsedDefNode BuildNode(
        XElement element,
        List<string> pathSegments,
        int depth,
        ref int traversalNodeCount,
        ref bool hitTraversalLimit)
    {
        if (depth > _options.MaxTraversalDepth)
        {
            hitTraversalLimit = true;
            return new ParsedDefNode(
                element.Name.LocalName,
                ResolveClassName(element),
                _pathBuilder.BuildRelativePath(pathSegments),
                pathSegments.ToArray(),
                string.Empty,
                depth,
                []);
        }

        traversalNodeCount++;
        if (traversalNodeCount > _options.MaxTraversalNodes)
        {
            hitTraversalLimit = true;
            return new ParsedDefNode(
                element.Name.LocalName,
                ResolveClassName(element),
                _pathBuilder.BuildRelativePath(pathSegments),
                pathSegments.ToArray(),
                string.Empty,
                depth,
                []);
        }

        var children = new List<ParsedDefNode>();
        foreach (var childWithSegment in BuildChildSegments(element))
        {
            if (hitTraversalLimit)
            {
                break;
            }

            var childPath = new List<string>(pathSegments.Count + 1);
            childPath.AddRange(pathSegments);
            childPath.Add(childWithSegment.Segment);

            children.Add(BuildNode(
                childWithSegment.Element,
                childPath,
                depth + 1,
                ref traversalNodeCount,
                ref hitTraversalLimit));
        }

        var value = element.Value ?? string.Empty;

        return new ParsedDefNode(
            element.Name.LocalName,
            ResolveClassName(element),
            _pathBuilder.BuildRelativePath(pathSegments),
            pathSegments.ToArray(),
            value,
            depth,
            children);
    }

    private static string ResolveDefType(XElement defElement)
    {
        var classAttr = defElement.Attribute("Class")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(classAttr))
        {
            return classAttr;
        }

        return defElement.Name.LocalName;
    }

    private static string ResolveDefName(XElement defElement)
    {
        var defName = defElement.Elements()
            .FirstOrDefault(x => x.Name.LocalName.Equals("defName", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

        if (!string.IsNullOrWhiteSpace(defName))
        {
            return defName;
        }

        var nameAttr = defElement.Attribute("Name")?.Value?.Trim();
        return nameAttr ?? string.Empty;
    }

    private static string ResolveClassName(XElement element)
    {
        var className = element.Attribute("Class")?.Value?.Trim();
        return className ?? string.Empty;
    }

    private static IReadOnlyList<(XElement Element, string Segment)> BuildChildSegments(XElement parent)
    {
        var elements = parent.Elements().ToList();
        if (elements.Count == 0)
        {
            return [];
        }

        var parentTagName = parent.Name.LocalName;
        if (!ShouldUseHandleSegments(parentTagName))
        {
            var basic = new List<(XElement Element, string Segment)>(elements.Count);
            var liIndex = 0;
            foreach (var child in elements)
            {
                var segment = child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)
                    ? liIndex++.ToString(CultureInfo.InvariantCulture)
                    : child.Name.LocalName;
                basic.Add((child, segment));
            }

            return basic;
        }

        var listItemOrdinalByElement = new Dictionary<XElement, int>();
        var listItemHandleByElement = new Dictionary<XElement, string>();
        var handleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var listItemIndex = 0;

        foreach (var child in elements)
        {
            if (!child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            listItemOrdinalByElement[child] = listItemIndex++;
            var handle = TryResolveListHandle(child, parentTagName);
            if (string.IsNullOrWhiteSpace(handle))
            {
                continue;
            }

            listItemHandleByElement[child] = handle;
            handleCounts[handle] = handleCounts.GetValueOrDefault(handle) + 1;
        }

        var handleOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(XElement Element, string Segment)>(elements.Count);

        foreach (var child in elements)
        {
            if (!child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                result.Add((child, child.Name.LocalName));
                continue;
            }

            if (!listItemHandleByElement.TryGetValue(child, out var handle))
            {
                var ordinal = listItemOrdinalByElement[child];
                result.Add((child, ordinal.ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            if (handleCounts.TryGetValue(handle, out var count) && count > 1)
            {
                var occurrence = handleOccurrence.GetValueOrDefault(handle);
                handleOccurrence[handle] = occurrence + 1;
                result.Add((child, $"{handle}-{occurrence.ToString(CultureInfo.InvariantCulture)}"));
                continue;
            }

            result.Add((child, handle));
        }

        return result;
    }

    private static bool ShouldUseHandleSegments(string parentTagName)
    {
        return parentTagName.Equals("parts", StringComparison.OrdinalIgnoreCase) ||
               parentTagName.Equals("comps", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolveListHandle(XElement listItem, string parentTagName)
    {
        var tKey = GetAttributeValue(listItem, "TKey");
        if (!string.IsNullOrWhiteSpace(tKey))
        {
            var normalized = NormalizeHandle(tKey);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        if (parentTagName.Equals("comps", StringComparison.OrdinalIgnoreCase))
        {
            var compClass = GetElementValue(listItem, "compClass");
            if (!string.IsNullOrWhiteSpace(compClass))
            {
                var normalized = NormalizeHandle(ExtractTypeShortName(compClass));
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
        }

        var key = GetElementValue(listItem, "key");
        if (!string.IsNullOrWhiteSpace(key))
        {
            var normalized = NormalizeHandle(key);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        if (parentTagName.Equals("parts", StringComparison.OrdinalIgnoreCase))
        {
            var partDef = GetElementValue(listItem, "def") ?? GetElementValue(listItem, "defName");
            if (!string.IsNullOrWhiteSpace(partDef))
            {
                var normalized = NormalizeHandle(partDef);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
        }

        var className = GetAttributeValue(listItem, "Class") ?? GetElementValue(listItem, "class");
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        var candidate = BuildHandleCandidateFromClassName(className, parentTagName);
        return NormalizeHandle(candidate);
    }

    private static string? GetAttributeValue(XElement element, string attributeName)
    {
        var attr = element.Attributes()
            .FirstOrDefault(x => x.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
        return attr?.Value?.Trim();
    }

    private static string? GetElementValue(XElement element, string childName)
    {
        var child = element.Elements()
            .FirstOrDefault(x => x.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase));
        return child?.Value?.Trim();
    }

    private static string BuildHandleCandidateFromClassName(string className, string parentTagName)
    {
        var shortName = ExtractTypeShortName(className);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return className;
        }

        if (parentTagName.Equals("parts", StringComparison.OrdinalIgnoreCase) &&
            shortName.StartsWith("ScenPart_", StringComparison.OrdinalIgnoreCase))
        {
            return shortName["ScenPart_".Length..];
        }

        if (parentTagName.Equals("comps", StringComparison.OrdinalIgnoreCase) &&
            shortName.StartsWith("CompProperties_", StringComparison.OrdinalIgnoreCase))
        {
            return $"Comp{shortName["CompProperties_".Length..]}";
        }

        return shortName;
    }

    private static string ExtractTypeShortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        var assemblySeparator = text.IndexOf(',');
        if (assemblySeparator >= 0)
        {
            text = text[..assemblySeparator];
        }

        var plusIndex = text.LastIndexOf('+');
        if (plusIndex >= 0 && plusIndex < text.Length - 1)
        {
            text = text[(plusIndex + 1)..];
        }

        var dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < text.Length - 1)
        {
            text = text[(dotIndex + 1)..];
        }

        return text.Trim();
    }

    private static string NormalizeHandle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var handle = raw.Trim()
            .Replace(' ', '_')
            .Replace('\n', '_')
            .Replace("\r", string.Empty)
            .Replace('\t', '_')
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (handle.IndexOf('{') >= 0)
        {
            handle = StringFormatSymbolsRegex.Replace(handle, string.Empty);
        }

        var filtered = new StringBuilder(handle.Length);
        foreach (var ch in handle)
        {
            if (HandleAllowedCharacters.IndexOf(ch) >= 0)
            {
                filtered.Append(ch);
            }
        }

        var compact = new StringBuilder(filtered.Length);
        for (var i = 0; i < filtered.Length; i++)
        {
            if (i == 0 || filtered[i] != '_' || filtered[i - 1] != '_')
            {
                compact.Append(filtered[i]);
            }
        }

        var normalized = compact.ToString().Trim('_');
        if (!string.IsNullOrWhiteSpace(normalized) && normalized.All(char.IsDigit))
        {
            normalized = $"_{normalized}";
        }

        return normalized;
    }
}
