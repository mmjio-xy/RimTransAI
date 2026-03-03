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
    }

    [Fact]
    public void TryRegister_DifferentScope_AllowsSamePath()
    {
        var registry = new FileRegistry();

        var first = registry.TryRegister("mod-a", "Languages/English/Keyed/keys.xml");
        var second = registry.TryRegister("mod-b", "Languages/English/Keyed/keys.xml");

        first.Should().BeTrue();
        second.Should().BeTrue();
    }
}
