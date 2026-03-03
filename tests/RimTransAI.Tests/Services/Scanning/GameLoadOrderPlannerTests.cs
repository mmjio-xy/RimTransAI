using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class GameLoadOrderPlannerTests
{
    [Fact]
    public void Plan_Stage0_ReturnsEmptyPlan()
    {
        var planner = new GameLoadOrderPlanner();
        var context = new ScanContext(
            "C:\\Mod",
            "English",
            "English",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var plan = planner.Plan(context);

        plan.Should().BeEmpty();
    }
}
