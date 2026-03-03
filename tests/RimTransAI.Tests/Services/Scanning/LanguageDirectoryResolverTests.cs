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

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_lang_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
