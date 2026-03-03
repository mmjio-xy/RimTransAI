using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class FileRegistryTests
{
    [Fact]
    public void TryRegister_SameScopeAndPath_ReturnsFalseOnSecondTime()
    {
        var registry = new FileRegistry();

        var first = registry.TryRegister("mod-a", "Languages/English/Keyed/keys.xml");
        var second = registry.TryRegister("mod-a", "Languages\\English\\Keyed\\keys.xml");

        first.Should().BeTrue();
        second.Should().BeFalse();
        registry.AttemptCount.Should().Be(2);
        registry.RegisteredCount.Should().Be(1);
        registry.DuplicateCount.Should().Be(1);
    }

    [Fact]
    public void TryRegister_DifferentScope_AllowsSamePath()
    {
        var registry = new FileRegistry();

        var first = registry.TryRegister("mod-a", "Languages/English/Keyed/keys.xml");
        var second = registry.TryRegister("mod-b", "Languages/English/Keyed/keys.xml");

        first.Should().BeTrue();
        second.Should().BeTrue();
        registry.AttemptCount.Should().Be(2);
        registry.RegisteredCount.Should().Be(2);
        registry.DuplicateCount.Should().Be(0);
    }

    [Fact]
    public void Clear_ResetsCounters()
    {
        var registry = new FileRegistry();
        registry.TryRegister("mod-a", "a.xml");
        registry.TryRegister("mod-a", "a.xml");

        registry.Clear();

        registry.AttemptCount.Should().Be(0);
        registry.RegisteredCount.Should().Be(0);
        registry.DuplicateCount.Should().Be(0);
    }
}
