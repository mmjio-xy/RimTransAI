using System.Collections.Generic;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public sealed record ScanContext(
    string ModRootPath,
    string LanguageFolderName,
    string LegacyLanguageFolderName,
    IReadOnlyCollection<string> ActivePackageIds);

public sealed record LoadFolderPlanEntry(
    string FullPath,
    string RelativePath,
    string Version,
    int Order);

public sealed record LanguageDirectoryEntry(
    string FullPath,
    string LoadFolderPath,
    string Version,
    int Order);

public sealed record XmlSourceFile(
    string FullPath,
    string RelativePath,
    string Version,
    string SourceKind,
    int Order);

public sealed class XmlSourceCollection
{
    public List<XmlSourceFile> DefFiles { get; } = [];

    public List<XmlSourceFile> KeyedFiles { get; } = [];

    public List<XmlSourceFile> DefInjectedFiles { get; } = [];
}

public sealed class ScanDiagnostics
{
    public int LoadFolderCount { get; set; }

    public int LanguageDirectoryCount { get; set; }

    public int DefFileCount { get; set; }

    public int KeyedFileCount { get; set; }

    public int DefInjectedFileCount { get; set; }
}

public sealed class ScanResult
{
    public XmlSourceCollection Sources { get; init; } = new();

    public List<TranslationItem> Items { get; init; } = [];

    public ScanDiagnostics Diagnostics { get; init; } = new();
}
