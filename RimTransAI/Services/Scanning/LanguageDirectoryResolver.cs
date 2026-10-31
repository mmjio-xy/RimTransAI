using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTransAI.Services.Scanning;

public sealed class LanguageDirectoryResolver
{
    private readonly List<string> _detectedLanguageArchives = [];

    /// <summary>
    /// 1.7 的 AbstractFilesystem 允许把语言目录打包成同名 .tar：当 Languages 下存在 X.tar 时，
    /// 游戏读取 tar 内容并忽略同名 X 目录。这里记录命中的压缩包路径，供上层提示用户。
    /// </summary>
    public IReadOnlyList<string> DetectedLanguageArchives => _detectedLanguageArchives;

    public IReadOnlyList<LanguageDirectoryEntry> Resolve(
        ScanContext context,
        IReadOnlyList<LoadFolderPlanEntry> loadFolders)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(loadFolders);

        _detectedLanguageArchives.Clear();
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
            RecordLanguageArchive(languagesRoot, context.LanguageFolderName);
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
            RecordLanguageArchive(languagesRoot, context.LegacyLanguageFolderName);
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

    private void RecordLanguageArchive(string languagesRoot, string languageFolderName)
    {
        if (string.IsNullOrWhiteSpace(languageFolderName) || !Directory.Exists(languagesRoot))
        {
            return;
        }

        string? archivePath;
        try
        {
            archivePath = Directory
                .EnumerateFiles(languagesRoot, "*.tar", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file => string.Equals(
                    Path.GetFileNameWithoutExtension(file),
                    languageFolderName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // 语言目录不可读时保持静默：扫描本身会给出其它告警。
            return;
        }

        if (archivePath == null ||
            _detectedLanguageArchives.Contains(archivePath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _detectedLanguageArchives.Add(archivePath);
    }
}
