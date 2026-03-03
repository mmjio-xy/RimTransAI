using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class XmlSourceCollector
{
    /// <summary>
    /// 阶段 0 脚手架：阶段 2/3 会补全 Keyed/DefInjected/Defs 源索引收集。
    /// </summary>
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
        return new XmlSourceCollection();
    }
}
