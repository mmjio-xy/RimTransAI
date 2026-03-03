using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class FileGeneratorService
{
    /// <summary>
    /// 保存翻译结果到文件，并附带原文注释
    /// </summary>
    public int GenerateFiles(string modRootPath, string targetLang, IEnumerable<TranslationItem> items)
    {
        var validItems = items
            .Where(x => !string.IsNullOrWhiteSpace(x.TranslatedText) && x.Status == "已翻译")
            .ToList();
        if (validItems.Count == 0)
        {
            return 0;
        }

        var groupedByTargetPath = validItems
            .Select(item => new
            {
                Item = item,
                TargetPath = DetermineOutputPath(modRootPath, item.FilePath, item.DefType, item.Version, targetLang)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetPath))
            .GroupBy(x => x.TargetPath!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filesCount = 0;
        foreach (var group in groupedByTargetPath)
        {
            var targetPath = group.Key;
            var groupItems = group.Select(x => x.Item).ToList();
            if (groupItems.Count == 0)
            {
                continue;
            }

            var isBackstoryFile = groupItems.All(x =>
                x.DefType.Equals("BackstoryDef", StringComparison.OrdinalIgnoreCase));

            var doc = isBackstoryFile
                ? BuildBackstoryDocument(groupItems)
                : BuildLanguageDataDocument(groupItems);

            try
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(targetPath))
                {
                    var fileInfo = new FileInfo(targetPath);
                    if (fileInfo.IsReadOnly)
                    {
                        fileInfo.IsReadOnly = false;
                    }
                }

                doc.Save(targetPath);
                filesCount++;
                Logger.Debug($"已保存: {targetPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"写入文件失败 {targetPath}", ex);
            }
        }

        return filesCount;
    }

    private string DetermineOutputPath(string modRoot, string originalPath, string defType, string version, string targetLang)
    {
        var contentRoot = string.IsNullOrEmpty(version) ? modRoot : Path.Combine(modRoot, version);

        if (string.Equals(defType, "BackstoryDef", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(contentRoot, "Languages", targetLang, "Backstories", "Backstories.xml");
        }

        // 标准化路径
        var normalizedPath = (originalPath ?? string.Empty)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        // 处理 Keyed/CodeLinked
        var keyedPattern = $"{Path.DirectorySeparatorChar}Keyed{Path.DirectorySeparatorChar}";
        var codeLinkedPattern = $"{Path.DirectorySeparatorChar}CodeLinked{Path.DirectorySeparatorChar}";
        var keyedIndex = normalizedPath.LastIndexOf(keyedPattern, StringComparison.OrdinalIgnoreCase);
        var codeLinkedIndex = normalizedPath.LastIndexOf(codeLinkedPattern, StringComparison.OrdinalIgnoreCase);
        var keyedFolderIndex = Math.Max(keyedIndex, codeLinkedIndex);

        if (keyedFolderIndex != -1)
        {
            var selectedPattern = keyedFolderIndex == keyedIndex ? keyedPattern : codeLinkedPattern;
            var relativePath = normalizedPath[(keyedFolderIndex + selectedPattern.Length)..];
            return Path.Combine(contentRoot, "Languages", targetLang, "Keyed", relativePath);
        }

        // 处理 DefInjected/DefLinked（来源于语言目录时保留子目录结构）
        var defInjectedPattern = $"{Path.DirectorySeparatorChar}DefInjected{Path.DirectorySeparatorChar}";
        var defLinkedPattern = $"{Path.DirectorySeparatorChar}DefLinked{Path.DirectorySeparatorChar}";
        var defInjectedIndex = normalizedPath.LastIndexOf(defInjectedPattern, StringComparison.OrdinalIgnoreCase);
        var defLinkedIndex = normalizedPath.LastIndexOf(defLinkedPattern, StringComparison.OrdinalIgnoreCase);
        var defInjectedFolderIndex = Math.Max(defInjectedIndex, defLinkedIndex);
        if (defInjectedFolderIndex != -1)
        {
            var selectedPattern = defInjectedFolderIndex == defInjectedIndex ? defInjectedPattern : defLinkedPattern;
            var relativePath = normalizedPath[(defInjectedFolderIndex + selectedPattern.Length)..];
            return Path.Combine(contentRoot, "Languages", targetLang, "DefInjected", relativePath);
        }

        // 处理 DefInjected（来源于 Defs）
        if (!string.IsNullOrWhiteSpace(defType))
        {
            var defsPattern = $"{Path.DirectorySeparatorChar}Defs{Path.DirectorySeparatorChar}";
            var defsIndex = normalizedPath.LastIndexOf(defsPattern, StringComparison.OrdinalIgnoreCase);

            string relativeFileName;
            if (defsIndex != -1)
            {
                // 保留 Defs 后的子目录结构，防止同名文件冲突
                relativeFileName = normalizedPath[(defsIndex + defsPattern.Length)..];
            }
            else
            {
                relativeFileName = Path.GetFileName(normalizedPath);
            }

            return Path.Combine(contentRoot, "Languages", targetLang, "DefInjected", defType, relativeFileName);
        }

        // 处理虚拟路径/DLL
        if (normalizedPath.Contains(".dll", StringComparison.OrdinalIgnoreCase) || 
            normalizedPath.StartsWith("[") || 
            !Path.IsPathRooted(normalizedPath))
        {
            return Path.Combine(contentRoot, "Languages", targetLang, "Keyed", "Generated_Code.xml");
        }

        // 兜底
        return Path.Combine(contentRoot, "Languages", targetLang, "Keyed", "Misc_Generated.xml");
    }

    private static XDocument BuildLanguageDataDocument(List<TranslationItem> items)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"));
        var root = new XElement("LanguageData");
        doc.Add(root);

        root.Add(new XComment(" Generated by RimTransAI "));

        var dedupedByKey = new Dictionary<string, TranslationItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            dedupedByKey[item.Key] = item;
        }

        foreach (var key in dedupedByKey.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var item = dedupedByKey[key];
            try
            {
                var safeOriginal = (item.OriginalText ?? string.Empty).Replace("--", "- -");
                root.Add(new XComment($" EN: {safeOriginal} "));
                root.Add(new XElement(item.Key, item.TranslatedText));
            }
            catch (Exception ex)
            {
                Logger.Error($"XML 节点创建失败 (Key: {item.Key}) - {ex.Message}");
                root.Add(new XComment($" ERROR: Could not save key '{item.Key}' "));
            }
        }

        return doc;
    }

    private static XDocument BuildBackstoryDocument(List<TranslationItem> items)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"));
        var root = new XElement("BackstoryTranslations");
        doc.Add(root);

        root.Add(new XComment(" Generated by RimTransAI "));

        var groupedByIdentifier = items
            .Select(item =>
            {
                SplitBackstoryKey(item.Key, out var identifier, out var fieldName);
                return new { Item = item, Identifier = identifier, FieldName = fieldName };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Identifier) && !string.IsNullOrWhiteSpace(x.FieldName))
            .GroupBy(x => x.Identifier!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var identifierGroup in groupedByIdentifier)
        {
            var backstoryElement = new XElement(identifierGroup.Key);
            var perField = new Dictionary<string, TranslationItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in identifierGroup)
            {
                perField[entry.FieldName!] = entry.Item;
            }

            foreach (var fieldName in perField.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var item = perField[fieldName];
                var legacyFieldName = MapBackstoryFieldName(fieldName);
                if (string.IsNullOrWhiteSpace(legacyFieldName))
                {
                    continue;
                }

                backstoryElement.Add(new XElement(legacyFieldName, item.TranslatedText));
            }

            if (backstoryElement.HasElements)
            {
                root.Add(backstoryElement);
            }
        }

        return doc;
    }

    private static void SplitBackstoryKey(string key, out string identifier, out string fieldName)
    {
        identifier = string.Empty;
        fieldName = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var firstDot = key.IndexOf('.');
        if (firstDot <= 0 || firstDot >= key.Length - 1)
        {
            return;
        }

        identifier = key[..firstDot].Trim();
        fieldName = key[(firstDot + 1)..].Trim();
    }

    private static string MapBackstoryFieldName(string fieldName)
    {
        if (fieldName.Equals("description", StringComparison.OrdinalIgnoreCase))
        {
            return "desc";
        }

        if (fieldName.Equals("title", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("titleFemale", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("titleShort", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("titleShortFemale", StringComparison.OrdinalIgnoreCase))
        {
            return fieldName;
        }

        return string.Empty;
    }
}
