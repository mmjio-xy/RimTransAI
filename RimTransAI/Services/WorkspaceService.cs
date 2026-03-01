using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// 工作区服务：负责发现来源目录中的 Mod。
/// </summary>
public class WorkspaceService
{
    private readonly ModInfoService _modInfoService;
    private readonly IconCatalogService _iconCatalogService;

    public WorkspaceService(ModInfoService modInfoService, IconCatalogService iconCatalogService)
    {
        _modInfoService = modInfoService;
        _iconCatalogService = iconCatalogService;
    }

    public List<WorkspaceModItem> DiscoverModsFromSources(IEnumerable<ModSourceFolder> sourceFolders)
    {
        var result = new List<WorkspaceModItem>();

        foreach (var source in sourceFolders.Where(x => x.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(source.FolderPath) || !Directory.Exists(source.FolderPath))
            {
                continue;
            }

            var iconKind = _iconCatalogService.ResolveSourceIconKind(source.IconKey, source.Id);
            var iconKey = _iconCatalogService.ResolveIconKey(source.IconKey, source.Id);
            var sourceName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? Path.GetFileName(source.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : source.DisplayName;

            foreach (var candidatePath in EnumerateModCandidates(source.FolderPath))
            {
                try
                {
                    var modInfo = _modInfoService.LoadModInfo(candidatePath);
                    if (modInfo == null)
                    {
                        continue;
                    }

                    result.Add(new WorkspaceModItem
                    {
                        SourceFolderId = source.Id,
                        SourceDisplayName = sourceName,
                        SourceIconKind = iconKind,
                        SourceIconKey = iconKey,
                        ModPath = candidatePath,
                        Name = string.IsNullOrWhiteSpace(modInfo.Name) ? Path.GetFileName(candidatePath) : modInfo.Name,
                        PackageId = modInfo.PackageId,
                        Author = modInfo.Author,
                        Status = "未加载",
                        ItemCount = 0,
                        LastScanAt = "-"
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warning($"发现 Mod 失败: {candidatePath} - {ex.Message}");
                }
            }
        }

        return result
            .OrderBy(x => x.SourceDisplayName)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static IEnumerable<string> EnumerateModCandidates(string sourceFolderPath)
    {
        if (IsValidModFolder(sourceFolderPath))
        {
            yield return sourceFolderPath;
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceFolderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsValidModFolder(subDir))
            {
                yield return subDir;
            }
        }
    }

    private static bool IsValidModFolder(string path)
    {
        return File.Exists(Path.Combine(path, "About", "About.xml"));
    }
}
