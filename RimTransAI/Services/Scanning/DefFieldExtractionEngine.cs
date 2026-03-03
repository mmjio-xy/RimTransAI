using System;
using System.Collections.Generic;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public sealed class DefFieldExtractionEngine
{
    /// <summary>
    /// 阶段 0 脚手架：阶段 4 会替换为反射优先、规则引擎化的提取流程。
    /// </summary>
    public List<TranslationItem> Extract(
        ScanContext context,
        XmlSourceCollection sources,
        Dictionary<string, HashSet<string>> reflectionMap)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reflectionMap);
        return [];
    }
}
