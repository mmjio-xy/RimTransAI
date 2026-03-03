using System.Collections.Generic;
using RimTransAI.Models;

namespace RimTransAI.Services.Scanning;

public sealed record ScanContext(
    string ModRootPath,
    string LanguageFolderName,
    string LegacyLanguageFolderName,
    IReadOnlyCollection<string> ActivePackageIds,
    string CurrentGameVersion = "");

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

    public List<XmlSourceFile> BackstoryFiles { get; } = [];

    public List<XmlSourceFile> StringFiles { get; } = [];

    public List<XmlSourceFile> WordInfoFiles { get; } = [];
}

public sealed class ScanDiagnostics
{
    public int LoadFolderCount { get; set; }

    public int LanguageDirectoryCount { get; set; }

    public int DefFileCount { get; set; }

    public int KeyedFileCount { get; set; }

    public int DefInjectedFileCount { get; set; }

    public int BackstoryFileCount { get; set; }

    public int StringFileCount { get; set; }

    public int WordInfoFileCount { get; set; }

    public int SourceFileAttemptCount { get; set; }

    public int SourceFileRegisteredCount { get; set; }

    public int SourceFileDeduplicatedCount { get; set; }

    public int ExtractedItemCount { get; set; }

    public int ExtractionConflictCount { get; set; }

    public int ExtractionErrorCount { get; set; }
}

public sealed class ExtractionDiagnostics
{
    public int ExtractedItemCount { get; set; }

    public int ConflictCount { get; set; }

    public int ErrorCount { get; set; }
}

public sealed class ScanResult
{
    public XmlSourceCollection Sources { get; init; } = new();

    public List<TranslationItem> Items { get; init; } = [];

    public ScanDiagnostics Diagnostics { get; init; } = new();
}
