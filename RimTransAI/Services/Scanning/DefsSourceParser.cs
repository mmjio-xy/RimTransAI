using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        var liIndex = 0;

        foreach (var child in parent.Elements())
        {
            if (hitTraversalLimit)
            {
                break;
            }

            var segment = child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)
                ? liIndex++.ToString(CultureInfo.InvariantCulture)
                : child.Name.LocalName;

            nodes.Add(BuildNode(
                child,
                [segment],
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
        var liIndex = 0;

        foreach (var child in element.Elements())
        {
            if (hitTraversalLimit)
            {
                break;
            }

            var segment = child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)
                ? liIndex++.ToString(CultureInfo.InvariantCulture)
                : child.Name.LocalName;

            var childPath = new List<string>(pathSegments.Count + 1);
            childPath.AddRange(pathSegments);
            childPath.Add(segment);

            children.Add(BuildNode(
                child,
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

        return defName ?? string.Empty;
    }

    private static string ResolveClassName(XElement element)
    {
        var className = element.Attribute("Class")?.Value?.Trim();
        return className ?? string.Empty;
    }
}
