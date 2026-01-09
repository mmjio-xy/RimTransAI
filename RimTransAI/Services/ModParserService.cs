using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimTransAI.Models;
// 引入正则

namespace RimTransAI.Services;

public class ModParserService
{
    // 需要提取的字段名
    private static readonly HashSet<string> TranslatableTags = new() 
    { 
        "label", "description", "jobString", "reportString", "labelNoun", "text" 
    };

    // 需要忽略的字段
    private static readonly HashSet<string> IgnoredTags = new() 
    { 
        "defName", "tag", "texPath", "workerClass", "soundMeleeHit", "soundMeleeMiss" 
    };

    // 用于匹配版本号的正则 (匹配路径中的 \1.4\ 或 /1.5/ 等)
    private static readonly Regex VersionRegex = new Regex(@"[\\/](1\.\d)[\\/]", RegexOptions.Compiled);

    public List<TranslationItem> ScanModFolder(string modPath)
    {
        var items = new List<TranslationItem>();
        
        // 扫描整个 Mod 文件夹，而不仅仅是 Defs
        // 因为有些 Mod 把 Defs 放在版本文件夹里，如 /1.5/Defs/
        if (!Directory.Exists(modPath)) return items;

        var xmlFiles = Directory.GetFiles(modPath, "*.xml", SearchOption.AllDirectories);

        foreach (var file in xmlFiles)
        {
            // 简单过滤：只解析 Defs 目录下的，或者路径包含 Defs 的文件
            // 以防止解析到 About.xml 或 Manifest.xml
            if (!file.Contains("Defs", StringComparison.OrdinalIgnoreCase) && 
                !file.Contains("Patches", StringComparison.OrdinalIgnoreCase)) 
            {
                continue;
            }

            try
            {
                var doc = XDocument.Load(file);
                if (doc.Root == null) continue;

                // 提取版本号
                string version = GetVersionFromPath(file);

                foreach (var defNode in doc.Root.Elements())
                {
                    var defName = defNode.Element("defName")?.Value;
                    if (string.IsNullOrEmpty(defName)) continue;

                    ExtractFields(defNode, defName, "", items, file, version);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析文件出错 {file}: {ex.Message}");
            }
        }

        return items;
    }

    /// <summary>
    /// 从文件路径提取版本号
    /// </summary>
    private string GetVersionFromPath(string filePath)
    {
        // 尝试匹配路径中的 1.0, 1.1, 1.2 ... 1.5 等
        var match = VersionRegex.Match(filePath);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return ""; // 如果没匹配到，默认为通用版本 (空字符串表示根目录/通用)
    }

    private void ExtractFields(XElement element, string defName, string parentPath, List<TranslationItem> items, string filePath, string version)
    {
        int listIndex = 0;
        var elements = element.Elements().ToList();
        
        // 判断是否为列表
        bool isList = elements.Count > 0 && elements.All(e => e.Name.LocalName == "li");

        foreach (var child in elements)
        {
            var tagName = child.Name.LocalName;

            if (IgnoredTags.Contains(tagName)) continue;

            string currentSegment;
            if (isList || tagName == "li")
            {
                currentSegment = listIndex.ToString();
                listIndex++;
            }
            else
            {
                currentSegment = tagName;
            }

            string fullPath = string.IsNullOrEmpty(parentPath) 
                ? currentSegment 
                : $"{parentPath}.{currentSegment}";

            if (TranslatableTags.Contains(tagName) && !child.HasElements && !string.IsNullOrWhiteSpace(child.Value))
            {
                string finalKey = $"{defName}.{fullPath}";

                // 检查重复：同一版本下，同一个 Key 只保留一个
                // 注意：不同版本可能有相同的 Key，这是正常的，所以去重条件要加上 Version
                if (!items.Any(x => x.Key == finalKey && x.Version == version))
                {
                    items.Add(new TranslationItem
                    {
                        Key = finalKey,
                        OriginalText = child.Value.Trim(),
                        TranslatedText = "", 
                        Status = "未翻译",
                        FilePath = filePath,
                        Version = version // <--- 关键：赋值版本号
                    });
                }
            }
            else if (child.HasElements)
            {
                ExtractFields(child, defName, fullPath, items, filePath, version);
            }
        }
    }
}