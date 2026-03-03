using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class GameLoadOrderPlanner
{
    /// <summary>
    /// 阶段 0 脚手架：阶段 1 会按 RimWorld 语义实现完整加载目录规划。
    /// </summary>
    public IReadOnlyList<LoadFolderPlanEntry> Plan(ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [];
    }
}
