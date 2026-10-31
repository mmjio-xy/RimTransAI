using System;
using System.IO;
using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public sealed class RimWorldAssemblyLocatorTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "RimTransAI_Locator_" + Guid.NewGuid().ToString("N"));

    public RimWorldAssemblyLocatorTests()
    {
        Directory.CreateDirectory(_sandbox);
    }

    [Fact]
    public void FindInGameRoot_FindsNewLayoutAtGameRoot()
    {
        var gameRoot = Path.Combine(_sandbox, "RimWorld");
        Directory.CreateDirectory(gameRoot);
        var assembly = Path.Combine(gameRoot, RimWorldAssemblyLocator.AssemblyFileName);
        File.WriteAllText(assembly, "stub");

        RimWorldAssemblyLocator.FindInGameRoot(gameRoot).Should().Be(assembly);
    }

    [Fact]
    public void FindInGameRoot_FindsLegacyManagedLayout()
    {
        var gameRoot = Path.Combine(_sandbox, "RimWorld");
        var managed = Path.Combine(gameRoot, "RimWorldWin64_Data", "Managed");
        Directory.CreateDirectory(managed);
        var assembly = Path.Combine(managed, RimWorldAssemblyLocator.AssemblyFileName);
        File.WriteAllText(assembly, "stub");

        RimWorldAssemblyLocator.FindInGameRoot(gameRoot).Should().Be(assembly);
    }

    [Fact]
    public void SearchAncestors_FindsGameRootFromNestedModDirectory()
    {
        var gameRoot = Path.Combine(_sandbox, "RimWorld");
        var modDirectory = Path.Combine(gameRoot, "Mods", "ExampleMod", "1.7");
        Directory.CreateDirectory(modDirectory);
        var assembly = Path.Combine(gameRoot, RimWorldAssemblyLocator.AssemblyFileName);
        File.WriteAllText(assembly, "stub");

        RimWorldAssemblyLocator.SearchAncestors(modDirectory).Should().Be(assembly);
    }

    [Fact]
    public void Resolve_PrefersConfiguredAssembly()
    {
        var gameRoot = Path.Combine(_sandbox, "RimWorld");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, RimWorldAssemblyLocator.AssemblyFileName), "stub");

        var configured = Path.Combine(_sandbox, "Custom", RimWorldAssemblyLocator.AssemblyFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(configured)!);
        File.WriteAllText(configured, "stub");

        RimWorldAssemblyLocator.Resolve(configured).Should().Be(configured);
    }

    [Fact]
    public void IsValidAssembly_TrimsQuotesAndWhitespace()
    {
        var assembly = Path.Combine(_sandbox, RimWorldAssemblyLocator.AssemblyFileName);
        File.WriteAllText(assembly, "stub");

        RimWorldAssemblyLocator.IsValidAssembly($"  \"{assembly}\"  ").Should().BeTrue();
        RimWorldAssemblyLocator.IsValidAssembly(string.Empty).Should().BeFalse();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
