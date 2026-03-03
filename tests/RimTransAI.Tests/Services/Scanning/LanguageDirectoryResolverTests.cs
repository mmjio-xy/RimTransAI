using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class LanguageDirectoryResolverTests
{
    [Fact]
    public void Resolve_Stage0_ReturnsEmptyDirectories()
    {
        var resolver = new LanguageDirectoryResolver();
        var context = new ScanContext(
            "C:\\Mod",
            "English",
            "English",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var directories = resolver.Resolve(context, []);

        directories.Should().BeEmpty();
    }
}
