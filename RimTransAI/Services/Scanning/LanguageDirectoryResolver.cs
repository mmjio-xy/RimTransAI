using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTransAI.Services.Scanning;

public sealed class LanguageDirectoryResolver
{
    public IReadOnlyList<LanguageDirectoryEntry> Resolve(
        ScanContext context,
        IReadOnlyList<LoadFolderPlanEntry> loadFolders)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(loadFolders);

        var result = new List<LanguageDirectoryEntry>();
        var order = 0;

        foreach (var loadFolder in loadFolders.OrderBy(x => x.Order))
        {
            var languagesRoot = Path.Combine(loadFolder.FullPath, "Languages");
            if (!Directory.Exists(languagesRoot))
            {
                continue;
            }

            var languageDir = Path.Combine(languagesRoot, context.LanguageFolderName);
            if (Directory.Exists(languageDir))
            {
                result.Add(new LanguageDirectoryEntry(
                    languageDir,
                    loadFolder.FullPath,
                    loadFolder.Version,
                    order++));
                continue;
            }

            if (string.Equals(context.LanguageFolderName, context.LegacyLanguageFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var legacyLanguageDir = Path.Combine(languagesRoot, context.LegacyLanguageFolderName);
            if (Directory.Exists(legacyLanguageDir))
            {
                result.Add(new LanguageDirectoryEntry(
                    legacyLanguageDir,
                    loadFolder.FullPath,
                    loadFolder.Version,
                    order++));
            }
        }

        return result;
    }
}
