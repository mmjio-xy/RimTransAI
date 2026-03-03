using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class GameLoadOrderPlannerTests
{
    [Fact]
    public void Plan_WithoutLoadFolders_UsesExactVersionThenCommonThenRoot()
    {
        var root = CreateTempModRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "1.5"));
            Directory.CreateDirectory(Path.Combine(root, "Common"));

            var planner = new GameLoadOrderPlanner();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var plan = planner.Plan(context);

            plan.Select(x => x.RelativePath).Should().ContainInOrder("1.5", "Common", ".");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_WithoutLoadFolders_FallsBackToClosestCompatibleVersion()
    {
        var root = CreateTempModRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "1.3"));
            Directory.CreateDirectory(Path.Combine(root, "1.4"));
            Directory.CreateDirectory(Path.Combine(root, "1.6"));
            Directory.CreateDirectory(Path.Combine(root, "Common"));

            var planner = new GameLoadOrderPlanner();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var plan = planner.Plan(context);

            plan.Select(x => x.RelativePath).Should().ContainInOrder("1.4", "Common", ".");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_WithLoadFolders_UsesSelectedVersionWithReverseOrderAndConditions()
    {
        var root = CreateTempModRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));
            Directory.CreateDirectory(Path.Combine(root, "C"));

            File.WriteAllText(Path.Combine(root, "LoadFolders.xml"), """
                <loadFolders>
                  <v1.5>
                    <li>A</li>
                    <li IfModActive="mod.required">B</li>
                    <li IfModNotActive="mod.blocked">C</li>
                  </v1.5>
                </loadFolders>
                """);

            var planner = new GameLoadOrderPlanner();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "mod.required"
                },
                "1.5");

            var plan = planner.Plan(context);

            plan.Select(x => x.RelativePath).Should().ContainInOrder("C", "B", "A");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_WithLoadFolders_UsesDefaultWhenNoCompatibleVersion()
    {
        var root = CreateTempModRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "V2"));
            Directory.CreateDirectory(Path.Combine(root, "Common"));

            File.WriteAllText(Path.Combine(root, "LoadFolders.xml"), """
                <loadFolders>
                  <v2.0>
                    <li>V2</li>
                  </v2.0>
                  <default>
                    <li>Common</li>
                  </default>
                </loadFolders>
                """);

            var planner = new GameLoadOrderPlanner();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var plan = planner.Plan(context);

            plan.Select(x => x.RelativePath).Should().ContainSingle().Which.Should().Be("Common");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_WithLoadFolders_NormalizesSteamPackagePostfixInConditions()
    {
        var root = CreateTempModRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Gated"));

            File.WriteAllText(Path.Combine(root, "LoadFolders.xml"), """
                <loadFolders>
                  <v1.5>
                    <li IfModActive="author.modid">Gated</li>
                  </v1.5>
                </loadFolders>
                """);

            var planner = new GameLoadOrderPlanner();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "author.modid_steam"
                },
                "1.5");

            var plan = planner.Plan(context);

            plan.Select(x => x.RelativePath).Should().ContainSingle().Which.Should().Be("Gated");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
