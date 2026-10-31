using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class LanguageDirectoryResolverTests
{
    [Fact]
    public void Resolve_WhenPrimaryLanguageDirectoryExists_UsesPrimaryDirectory()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "English"));
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "EnglishLegacy"));

            var resolver = new LanguageDirectoryResolver();
            var context = new ScanContext(
                root,
                "English",
                "EnglishLegacy",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var result = resolver.Resolve(context, [
                new LoadFolderPlanEntry(loadFolder, "1.5", "1.5", 0)
            ]);

            result.Should().ContainSingle();
            result[0].FullPath.Should().EndWith(Path.Combine("Languages", "English"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenPrimaryMissingAndLegacyExists_UsesLegacyDirectory()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "EnglishLegacy"));

            var resolver = new LanguageDirectoryResolver();
            var context = new ScanContext(
                root,
                "English",
                "EnglishLegacy",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var result = resolver.Resolve(context, [
                new LoadFolderPlanEntry(loadFolder, "1.5", "1.5", 0)
            ]);

            result.Should().ContainSingle();
            result[0].FullPath.Should().EndWith(Path.Combine("Languages", "EnglishLegacy"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenLanguageArchiveExists_RecordsDetectedArchive()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.7");
            var languagesRoot = Path.Combine(loadFolder, "Languages");
            Directory.CreateDirectory(languagesRoot);
            var archive = Path.Combine(languagesRoot, "ChineseSimplified (简体中文).tar");
            File.WriteAllBytes(archive, [0x00]);

            var resolver = new LanguageDirectoryResolver();
            var context = new ScanContext(
                root,
                "ChineseSimplified (简体中文)",
                "ChineseSimplified",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.7");

            var result = resolver.Resolve(context, [
                new LoadFolderPlanEntry(loadFolder, "1.7", "1.7", 0)
            ]);

            result.Should().BeEmpty();
            resolver.DetectedLanguageArchives.Should().ContainSingle().Which.Should().Be(archive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenArchiveAndDirectoryBothExist_StillRecordsArchive()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.7");
            var languagesRoot = Path.Combine(loadFolder, "Languages");
            Directory.CreateDirectory(Path.Combine(languagesRoot, "ChineseSimplified"));
            var archive = Path.Combine(languagesRoot, "ChineseSimplified.tar");
            File.WriteAllBytes(archive, [0x00]);

            var resolver = new LanguageDirectoryResolver();
            var context = new ScanContext(
                root,
                "ChineseSimplified",
                "ChineseSimplified",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.7");

            var result = resolver.Resolve(context, [
                new LoadFolderPlanEntry(loadFolder, "1.7", "1.7", 0)
            ]);

            // 1.7 会读取 tar 并忽略同名目录：目录仍可用于提取，但必须提示用户输出不会生效。
            result.Should().ContainSingle();
            resolver.DetectedLanguageArchives.Should().ContainSingle().Which.Should().Be(archive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_lang_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
