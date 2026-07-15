using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class OperationLogBufferTests
{
    [Fact]
    public void Append_WhenCapacityIsExceeded_RemovesOldestEntries()
    {
        var buffer = new OperationLogBuffer(capacity: 3);

        buffer.Append("第一条\n第二条\n第三条\n第四条");

        buffer.Entries.Select(x => x.Message)
            .Should().Equal("第二条", "第三条", "第四条");
        buffer.LatestMessage.Should().Be("第四条");
    }

    [Fact]
    public void Replace_ClearsPreviousEntriesAndClassifiesNewLines()
    {
        var buffer = new OperationLogBuffer();
        buffer.Append("旧消息");

        buffer.Replace("翻译完成\n批次失败\n翻译已取消");

        buffer.Entries.Should().HaveCount(3);
        buffer.Entries[0].Level.Should().Be(OperationLogLevel.Success);
        buffer.Entries[1].Level.Should().Be(OperationLogLevel.Error);
        buffer.Entries[2].Level.Should().Be(OperationLogLevel.Warning);
    }

    [Fact]
    public void Constructor_WithInvalidCapacity_Throws()
    {
        var operation = () => new OperationLogBuffer(capacity: 0);

        operation.Should().Throw<ArgumentOutOfRangeException>();
    }
}
