using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class ModParserService
{
    // 需要提取的特定标签
    private static readonly HashSet<string> TargetTags = new() 
    { 
        "label", "description", "labelNoun", "jobString", "text", "reportString" 
    };

    /// <summary>
    /// 扫描 Mod 文件夹，支持多版本结构 (Root, 1.4, 1.5, Common 等)
    /// </summary>
    public List<TranslationItem> ScanModFolder(string modRootPath)
    {
        var results = new List<TranslationItem>();

        if (string.IsNullOrWhiteSpace(modRootPath) || !Directory.Exists(modRootPath))
            return results;

        // 1. 识别所有可能的“内容根目录”
        // 包括：Mod根目录本身, Common, 以及 1.0, 1.1 ... 1.9 等版本文件夹
        var contentRoots = GetPotentialContentRoots(modRootPath);

        foreach (var contentRoot in contentRoots)
        {
            // 获取相对路径版本号 (例如 "", "1.5", "Common")
            string versionStr = Path.GetRelativePath(modRootPath, contentRoot);
            if (versionStr == ".") versionStr = ""; // 根目录

            // A. 扫描 Defs (DefInjected)
            var defsDir = Path.Combine(contentRoot, "Defs");
            if (Directory.Exists(defsDir))
            {
                var defFiles = Directory.GetFiles(defsDir, "*.xml", SearchOption.AllDirectories);
                foreach (var file in defFiles)
                {
                    results.AddRange(ParseDefFile(file, versionStr));
                }
            }

            // B. 扫描 Keyed (Keyed) - 通常在 Languages/English/Keyed
            // 注意：有的 Mod 直接把 Languages 放在版本文件夹下，有的只放在根目录
            // 这里我们尝试在当前 contentRoot 下找
            var keyedDir = Path.Combine(contentRoot, "Languages", "English", "Keyed");
            
            // 如果没找到 English，尝试找原本自带的 ChineseSimplified (有时候做参考)
            // 这里暂时只找英文源
            if (Directory.Exists(keyedDir))
            {
                var keyedFiles = Directory.GetFiles(keyedDir, "*.xml", SearchOption.AllDirectories);
                foreach (var file in keyedFiles)
                {
                    results.AddRange(ParseKeyedFile(file, versionStr));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 获取 Mod 根目录下的所有有效内容目录（根目录 + 版本目录）
    /// </summary>
    private List<string> GetPotentialContentRoots(string modRoot)
    {
        var roots = new List<string> { modRoot }; // 首先包含根目录

        // 获取所有子目录
        var subDirs = Directory.GetDirectories(modRoot);
        
        // 正则匹配版本号文件夹 (如 "1.0", "1.5") 或 "Common"
        var versionRegex = new Regex(@"^(\d+\.\d+|Common)$", RegexOptions.IgnoreCase);

        foreach (var dir in subDirs)
        {
            var dirName = new DirectoryInfo(dir).Name;
            if (versionRegex.IsMatch(dirName))
            {
                roots.Add(dir);
            }
        }

        return roots;
    }

    private IEnumerable<TranslationItem> ParseDefFile(string filePath, string version)
    {
        XDocument doc;
        try
        {
            // 加载 XML 时忽略命名空间等复杂情况，简单粗暴处理
            doc = XDocument.Load(filePath);
        }
        catch (Exception) { yield break; }

        if (doc.Root == null) yield break;

        // 递归查找所有带有 defName 的节点
        // 这里使用 Descendants 是为了防止 Def 套 Def 的情况
        var allDefs = doc.Root.Descendants().Where(x => x.Element("defName") != null);

        foreach (var defNode in allDefs)
        {
            var defName = defNode.Element("defName")?.Value;
            if (string.IsNullOrWhiteSpace(defName)) continue;

            // 查找该 Def 下的目标标签
            foreach (var element in defNode.Descendants())
            {
                // 只处理叶子节点 (没有子节点的节点)，防止把 List 结构本身当成文本
                if (!element.HasElements && 
                    TargetTags.Contains(element.Name.LocalName) && 
                    !string.IsNullOrWhiteSpace(element.Value))
                {
                    // 构建 DefInjected Key
                    // 简单逻辑：DefName.标签名
                    // 复杂逻辑：需要处理 List 索引 (例如 stages.0.label)，这里先做简单版
                    
                    // 为了生成路径，我们需要知道这个 XML 相对于 Defs 文件夹的路径
                    // 比如: .../1.5/Defs/ThingDefs/Gun.xml -> ThingDefs/Gun.xml
                    // 这样将来生成 DefInjected 时才能保持目录结构
                    
                    yield return new TranslationItem
                    {
                        Key = $"{defName}.{element.Name.LocalName}",
                        OriginalText = element.Value,
                        FilePath = filePath,
                        Version = version,
                        Status = $"[{version}] 待翻译" // 在状态里标记版本，方便调试
                    };
                }
            }
        }
    }

    private IEnumerable<TranslationItem> ParseKeyedFile(string filePath, string version)
    {
        XDocument doc;
        try { doc = XDocument.Load(filePath); } catch { yield break; }
        if (doc.Root == null) yield break;

        foreach (var element in doc.Root.Elements())
        {
            if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            {
                yield return new TranslationItem
                {
                    Key = element.Name.LocalName,
                    OriginalText = element.Value,
                    FilePath = filePath,
                    Version = version,
                    Status = $"[{version}] 待翻译"
                };
            }
        }
    }
}