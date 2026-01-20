using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class LlmModelsTests
{
    [Fact]
    public void LlmRequest_DefaultValues_AreValid()
    {
        // Arrange & Act
        var request = new LlmRequest();

        // Assert
        request.Should().NotBeNull();
        request.model.Should().Be(string.Empty);
        request.messages.Should().HaveCount(0);
        request.temperature.Should().Be(0.3);
    }

    [Fact]
    public void LlmRequest_WithValidParameters_PropertiesAreSet()
    {
        // Arrange & Act
        var request = new LlmRequest
        {
            model = "gpt-3.5-turbo",
            messages = new System.Collections.Generic.List<LlmMessage>
            {
                new LlmMessage { role = "system", content = "You are a translator." },
                new LlmMessage { role = "user", content = "Translate this." }
            }
        };

        // Assert
        request.model.Should().Be("gpt-3.5-turbo");
        request.messages.Should().HaveCount(2);
        request.messages[0].role.Should().Be("system");
        request.messages[0].content.Should().Be("You are a translator.");
    }

    [Fact]
    public void LlmResponseFormat_DefaultValues_AreValid()
    {
        // Arrange & Act
        var format = new LlmResponseFormat();

        // Assert
        format.Should().NotBeNull();
        format.type.Should().Be("json_object");
    }

    [Fact]
    public void LlmMessage_CanBeInstantiated()
    {
        // Arrange & Act
        var message = new LlmMessage
        {
            role = "user",
            content = "Test content"
        };

        // Assert
        message.role.Should().Be("user");
        message.content.Should().Be("Test content");
    }

    [Fact]
    public void LlmMessage_DefaultValues_AreEmpty()
    {
        // Arrange & Act
        var message = new LlmMessage();

        // Assert
        message.role.Should().Be(string.Empty);
        message.content.Should().Be(string.Empty);
    }
}
