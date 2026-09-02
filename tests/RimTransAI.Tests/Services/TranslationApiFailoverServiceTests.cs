using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class TranslationApiFailoverServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_UsesConfiguredOrderAndSkipsDisabledEntries()
    {
        var calls = new List<string>();
        var llm = new StubLlmService((apiUrl, model, sourceTexts, _) =>
        {
            calls.Add($"{apiUrl}/{model}");
            if (model == "secondary-success")
            {
                return Task.FromResult(TranslateEverything(sourceTexts));
            }

            throw new InvalidOperationException("candidate failed");
        });
        var service = new TranslationApiFailoverService(llm);

        var result = await service.TranslateBatchAsync(
            [
                Api("Disabled API", "https://disabled.example", false, [Model("disabled-model")]),
                Api("Primary", "https://primary.example", true, [Model("disabled", false), Model("primary-one"), Model("primary-two")]),
                Api("Secondary", "https://secondary.example", true, [Model("secondary-disabled", false), Model("secondary-success")])
            ],
            SourceTexts(),
            "Chinese",
            null);

        result.Should().HaveCount(2);
        calls.Should().Equal(
            "https://primary.example/primary-one",
            "https://primary.example/primary-two",
            "https://secondary.example/secondary-success");
    }

    [Fact]
    public async Task TranslateBatchAsync_WhenAllCandidatesFail_ReportsDistinctApiNamesInOrder()
    {
        var llm = new StubLlmService((_, _, _, _) => throw new InvalidOperationException("API down"));
        var service = new TranslationApiFailoverService(llm);

        var action = () => service.TranslateBatchAsync(
            [
                Api("Primary", "https://primary.example", true, [Model("primary-one"), Model("primary-two")]),
                Api("Secondary", "https://secondary.example", true, [Model("secondary")])
            ],
            SourceTexts(),
            "Chinese",
            null);

        var exception = await FluentActions.Awaiting(action)
            .Should().ThrowAsync<TranslationApiFailoverException>();

        exception.Which.FailedApiNames.Should().Equal("Primary", "Secondary");
        exception.Which.Message.Should().Be("API 调用失败：【Primary、Secondary】");
    }

    [Fact]
    public async Task TranslateBatchAsync_WhenResponseIsIncomplete_UsesNextApi()
    {
        var calls = new List<string>();
        var llm = new StubLlmService((apiUrl, _, sourceTexts, _) =>
        {
            calls.Add(apiUrl);
            return Task.FromResult(apiUrl == "https://primary.example"
                ? new Dictionary<string, string> { ["one"] = "一" }
                : TranslateEverything(sourceTexts));
        });
        var service = new TranslationApiFailoverService(llm);

        var result = await service.TranslateBatchAsync(
            [
                Api("Primary", "https://primary.example", true, [Model("primary")]),
                Api("Secondary", "https://secondary.example", true, [Model("secondary")])
            ],
            SourceTexts(),
            "Chinese",
            null);

        result.Should().HaveCount(2);
        calls.Should().Equal("https://primary.example", "https://secondary.example");
    }

    [Fact]
    public async Task TranslateBatchAsync_WhenCancelled_DoesNotTryNextApi()
    {
        var calls = 0;
        var llm = new StubLlmService((_, _, _, _) =>
        {
            calls++;
            throw new OperationCanceledException();
        });
        var service = new TranslationApiFailoverService(llm);

        var action = () => service.TranslateBatchAsync(
            [
                Api("Primary", "https://primary.example", true, [Model("primary")]),
                Api("Secondary", "https://secondary.example", true, [Model("secondary")])
            ],
            SourceTexts(),
            "Chinese",
            null);

        await FluentActions.Awaiting(action).Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    private static TranslationApiProfile Api(
        string name,
        string url,
        bool enabled,
        List<TranslationApiModel> models) => new()
    {
        Name = name,
        IsEnabled = enabled,
        ApiUrl = url,
        ApiKey = "test-key",
        Models = models
    };

    private static TranslationApiModel Model(string modelId, bool enabled = true) => new()
    {
        ModelId = modelId,
        IsEnabled = enabled
    };

    private static Dictionary<string, string> SourceTexts() => new()
    {
        ["one"] = "one",
        ["two"] = "two"
    };

    private static Dictionary<string, string> TranslateEverything(Dictionary<string, string> sourceTexts) =>
        sourceTexts.ToDictionary(pair => pair.Key, pair => $"translated-{pair.Value}");

    private sealed class StubLlmService(
        Func<string, string, Dictionary<string, string>, CancellationToken, Task<Dictionary<string, string>>> handler)
        : LlmService
    {
        public override Task<Dictionary<string, string>> TranslateBatchAsync(
            string apiKey,
            Dictionary<string, string> sourceTexts,
            string apiUrl,
            string model,
            string targetLang = "Simplified Chinese",
            string? customPrompt = null,
            int requestTimeoutSeconds = 480,
            bool autoCompleteApiUrl = true,
            CancellationToken cancellationToken = default)
        {
            return handler(apiUrl, model, sourceTexts, cancellationToken);
        }
    }
}
