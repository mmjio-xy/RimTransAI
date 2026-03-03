using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTransAI.Services.Scanning;

public sealed class XmlSourceCollector
{
    public XmlSourceCollection Collect(
        ScanContext context,
        IReadOnlyList<LoadFolderPlanEntry> loadFolders,
        IReadOnlyList<LanguageDirectoryEntry> languageDirectories,
        FileRegistry fileRegistry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(loadFolders);
        ArgumentNullException.ThrowIfNull(languageDirectories);
        ArgumentNullException.ThrowIfNull(fileRegistry);

        var sources = new XmlSourceCollection();

        CollectDefs(loadFolders, fileRegistry, sources);
        CollectLanguageFiles(languageDirectories, fileRegistry, sources);

        return sources;
    }

    private static void CollectDefs(
        IReadOnlyList<LoadFolderPlanEntry> loadFolders,
        FileRegistry fileRegistry,
        XmlSourceCollection sources)
    {
        var order = 0;
        foreach (var loadFolder in loadFolders.OrderBy(x => x.Order))
        {
            var defsDir = Path.Combine(loadFolder.FullPath, "Defs");
            if (!Directory.Exists(defsDir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(defsDir, "*.xml", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(loadFolder.FullPath, file));
                if (!fileRegistry.TryRegister("defs", relativePath))
                {
                    continue;
                }

                sources.DefFiles.Add(new XmlSourceFile(
                    Path.GetFullPath(file),
                    relativePath,
                    loadFolder.Version,
                    "Defs",
                    order++));
            }
        }
    }

    private static void CollectLanguageFiles(
        IReadOnlyList<LanguageDirectoryEntry> languageDirectories,
        FileRegistry fileRegistry,
        XmlSourceCollection sources)
    {
        var order = 0;
        foreach (var languageDir in languageDirectories.OrderBy(x => x.Order))
        {
            CollectKeyed(languageDir, fileRegistry, sources, ref order);
            CollectDefInjected(languageDir, fileRegistry, sources, ref order);
            CollectStrings(languageDir, fileRegistry, sources, ref order);
            CollectWordInfo(languageDir, fileRegistry, sources, ref order);
        }
    }

    private static void CollectKeyed(
        LanguageDirectoryEntry languageDir,
        FileRegistry fileRegistry,
        XmlSourceCollection sources,
        ref int order)
    {
        var codeLinkedDir = Path.Combine(languageDir.FullPath, "CodeLinked");
        var keyedDir = Path.Combine(languageDir.FullPath, "Keyed");
        string? selected = null;
        if (Directory.Exists(codeLinkedDir))
        {
            selected = codeLinkedDir;
        }
        else if (Directory.Exists(keyedDir))
        {
            selected = keyedDir;
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(selected, "*.xml", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(languageDir.LoadFolderPath, file));
            if (!fileRegistry.TryRegister("lang", relativePath))
            {
                continue;
            }

            sources.KeyedFiles.Add(new XmlSourceFile(
                Path.GetFullPath(file),
                relativePath,
                languageDir.Version,
                "Keyed",
                order++));
        }
    }

    private static void CollectDefInjected(
        LanguageDirectoryEntry languageDir,
        FileRegistry fileRegistry,
        XmlSourceCollection sources,
        ref int order)
    {
        var defLinkedDir = Path.Combine(languageDir.FullPath, "DefLinked");
        var defInjectedDir = Path.Combine(languageDir.FullPath, "DefInjected");
        string? selected = null;
        if (Directory.Exists(defLinkedDir))
        {
            selected = defLinkedDir;
        }
        else if (Directory.Exists(defInjectedDir))
        {
            selected = defInjectedDir;
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        foreach (var typeDir in Directory.EnumerateDirectories(selected, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var defTypeName = Path.GetFileName(typeDir);
            foreach (var file in Directory.EnumerateFiles(typeDir, "*.xml", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(languageDir.LoadFolderPath, file));
                if (!fileRegistry.TryRegister("lang", relativePath))
                {
                    continue;
                }

                sources.DefInjectedFiles.Add(new XmlSourceFile(
                    Path.GetFullPath(file),
                    relativePath,
                    languageDir.Version,
                    $"DefInjected:{defTypeName}",
                    order++));
            }
        }
    }

    private static void CollectStrings(
        LanguageDirectoryEntry languageDir,
        FileRegistry fileRegistry,
        XmlSourceCollection sources,
        ref int order)
    {
        var stringsDir = Path.Combine(languageDir.FullPath, "Strings");
        if (!Directory.Exists(stringsDir))
        {
            return;
        }

        foreach (var topDir in Directory.EnumerateDirectories(stringsDir, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(topDir, "*.txt", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(languageDir.LoadFolderPath, file));
                if (!fileRegistry.TryRegister("lang", relativePath))
                {
                    continue;
                }

                sources.StringFiles.Add(new XmlSourceFile(
                    Path.GetFullPath(file),
                    relativePath,
                    languageDir.Version,
                    "Strings",
                    order++));
            }
        }
    }

    private static void CollectWordInfo(
        LanguageDirectoryEntry languageDir,
        FileRegistry fileRegistry,
        XmlSourceCollection sources,
        ref int order)
    {
        var wordInfoDir = Path.Combine(languageDir.FullPath, "WordInfo");
        if (!Directory.Exists(wordInfoDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(wordInfoDir, "*.txt", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(languageDir.LoadFolderPath, file));
            if (!fileRegistry.TryRegister("lang", relativePath))
            {
                continue;
            }

            sources.WordInfoFiles.Add(new XmlSourceFile(
                Path.GetFullPath(file),
                relativePath,
                languageDir.Version,
                "WordInfo",
                order++));
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/')
            .TrimEnd('/');
    }
}
