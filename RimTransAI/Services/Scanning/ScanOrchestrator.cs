using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class ScanOrchestrator
{
    private readonly GameLoadOrderPlanner _loadOrderPlanner;
    private readonly LanguageDirectoryResolver _languageDirectoryResolver;
    private readonly XmlSourceCollector _xmlSourceCollector;
    private readonly DefFieldExtractionEngine _defFieldExtractionEngine;
    private readonly FileRegistry _fileRegistry;

    public ScanOrchestrator(
        GameLoadOrderPlanner? loadOrderPlanner = null,
        LanguageDirectoryResolver? languageDirectoryResolver = null,
        XmlSourceCollector? xmlSourceCollector = null,
        DefFieldExtractionEngine? defFieldExtractionEngine = null,
        FileRegistry? fileRegistry = null)
    {
        _loadOrderPlanner = loadOrderPlanner ?? new GameLoadOrderPlanner();
        _languageDirectoryResolver = languageDirectoryResolver ?? new LanguageDirectoryResolver();
        _xmlSourceCollector = xmlSourceCollector ?? new XmlSourceCollector();
        _defFieldExtractionEngine = defFieldExtractionEngine ?? new DefFieldExtractionEngine();
        _fileRegistry = fileRegistry ?? new FileRegistry();
    }

    /// <summary>
    /// 阶段 0 脚手架：先贯通模块依赖关系，后续阶段填充完整语义。
    /// </summary>
    public ScanResult Scan(ScanContext context, Dictionary<string, HashSet<string>> reflectionMap)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reflectionMap);

        _fileRegistry.Clear();

        var loadFolders = _loadOrderPlanner.Plan(context);
        var languageDirectories = _languageDirectoryResolver.Resolve(context, loadFolders);
        var sources = _xmlSourceCollector.Collect(context, loadFolders, languageDirectories, _fileRegistry);
        var items = _defFieldExtractionEngine.Extract(context, sources, reflectionMap);

        return new ScanResult
        {
            Sources = sources,
            Items = items,
            Diagnostics = new ScanDiagnostics
            {
                LoadFolderCount = loadFolders.Count,
                LanguageDirectoryCount = languageDirectories.Count,
                DefFileCount = sources.DefFiles.Count,
                KeyedFileCount = sources.KeyedFiles.Count,
                DefInjectedFileCount = sources.DefInjectedFiles.Count
            }
        };
    }
}
