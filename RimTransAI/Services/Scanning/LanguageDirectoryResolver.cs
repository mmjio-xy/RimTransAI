using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class LanguageDirectoryResolver
{
    /// <summary>
    /// 阶段 0 脚手架：阶段 2 会实现 folderName / legacyFolderName 精确匹配语义。
    /// </summary>
    public IReadOnlyList<LanguageDirectoryEntry> Resolve(
        ScanContext context,
        IReadOnlyList<LoadFolderPlanEntry> loadFolders)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(loadFolders);
        return [];
    }
}
