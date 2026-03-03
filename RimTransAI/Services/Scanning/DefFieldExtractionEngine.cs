using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public sealed class DefFieldExtractionEngine
{
    public List<TranslationItem> Extract(
        ScanContext context,
        XmlSourceCollection sources,
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reflectionMap);

        var extractor = new TranslationExtractor(reflectionMap);
        var results = new List<TranslationItem>();

        ExtractDefs(extractor, sources.DefFiles, results);
        ExtractKeyed(sources.KeyedFiles, results);

        return results;
    }

    private static void ExtractDefs(
        TranslationExtractor extractor,
        IReadOnlyList<XmlSourceFile> defFiles,
        List<TranslationItem> output)
    {
        var upsert = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in defFiles.OrderBy(x => x.Order))
        {
            try
            {
                var doc = XDocument.Load(source.FullPath);
                if (doc.Root == null || !doc.Root.Name.LocalName.Equals("Defs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var units = extractor.Extract(doc.Root.Elements(), source.FullPath, source.Version);
                foreach (var unit in units)
                {
                    var item = new TranslationItem
                    {
                        Key = unit.Key,
                        DefType = unit.DefType,
                        OriginalText = unit.OriginalText,
                        TranslatedText = string.Empty,
                        Status = "未翻译",
                        FilePath = unit.SourceFile,
                        Version = unit.Version
                    };

                    var dedupeKey = $"defs|{item.DefType}|{item.Version}|{item.Key}";
                    Upsert(output, upsert, dedupeKey, item);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Defs XML 格式错误 {source.FullPath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Defs 文件出错: {source.FullPath}", ex);
            }
        }
    }

    private static void ExtractKeyed(
        IReadOnlyList<XmlSourceFile> keyedFiles,
        List<TranslationItem> output)
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
                        Version = source.Version
                    };

                    var dedupeKey = $"keyed|{item.Key}";
                    Upsert(output, upsert, dedupeKey, item);
                }
            }
            catch (XmlException ex)
            {
                Logger.Warning($"Keyed XML 格式错误 {source.FullPath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"解析 Keyed 文件出错: {source.FullPath}", ex);
            }
        }
    }

    private static void Upsert(
        List<TranslationItem> target,
        Dictionary<string, int> indexMap,
        string key,
        TranslationItem value)
    {
        if (indexMap.TryGetValue(key, out var existingIndex))
        {
            target[existingIndex] = value;
            return;
        }

        indexMap[key] = target.Count;
        target.Add(value);
    }
}
