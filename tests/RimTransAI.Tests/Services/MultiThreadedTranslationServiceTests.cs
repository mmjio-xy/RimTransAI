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

public class MultiThreadedTranslationServiceTests
{
    [Fact]
    public async Task ExecuteBatchesAsync_WhenPrimaryApiFails_UsesNextApi()
    {
        var firstItem = CreateItem("first");
        var secondItem = CreateItem("second");
        var batchResult = CreateBatchResult(firstItem, secondItem);
        var calls = new List<string>();
        var llmService = new StubLlmService((apiUrl, _, sourceTexts, _) =>
        {
            calls.Add(apiUrl);
            if (apiUrl == "https://primary.example")
            {
                throw new InvalidOperationException("primary failure");
            }

            return Task.FromResult(sourceTexts.ToDictionary(x => x.Key, _ => "翻译成功"));
        });

        using var concurrencyManager = new ConcurrencyManager(1);
        using var progressReporter = new ThreadSafeProgressReporter();
        using var service = new MultiThreadedTranslationService(llmService);

        var successfulBatches = await service.ExecuteBatchesAsync(
            batchResult,
            concurrencyManager,
            progressReporter,
            [Api("Primary", "https://primary.example"), Api("Secondary", "https://secondary.example")],
            "Chinese");

        successfulBatches.Should().Be(2);
        firstItem.Status.Should().Be("已翻译");
        secondItem.Status.Should().Be("已翻译");
        calls.Should().Equal(
            "https://primary.example",
            "https://secondary.example",
            "https://primary.example",
            "https://secondary.example");
    }

    [Fact]
    public async Task ExecuteBatchesAsync_WhenRequestIsCancelled_PropagatesCancellation()
    {
        var item = CreateItem("cancel-me");
        var batchResult = CreateBatchResult(item);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var llmService = new StubLlmService(async (_, _, _, cancellationToken) =>
        {
            requestStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new Dictionary<string, string>();
        });

        using var concurrencyManager = new ConcurrencyManager(1);
        using var progressReporter = new ThreadSafeProgressReporter();
        using var service = new MultiThreadedTranslationService(llmService);
        using var cts = new CancellationTokenSource();

        var execution = service.ExecuteBatchesAsync(
            batchResult,
            concurrencyManager,
            progressReporter,
            [Api("Primary", "https://primary.example")],
            "Chinese",
            cancellationToken: cts.Token);

        await requestStarted.Task;
        cts.Cancel();

        await FluentActions.Awaiting(() => execution)
            .Should().ThrowAsync<OperationCanceledException>();
        item.Status.Should().Be("等待中");
    }

    [Fact]
    public async Task ExecuteBatchesAsync_WhenFirstResponseIsIncomplete_UsesNextApi()
    {
        var item = CreateItem("missing");
        var batchResult = CreateBatchResult(item);
        var llmService = new StubLlmService((apiUrl, _, sourceTexts, _) =>
            Task.FromResult(apiUrl == "https://primary.example"
                ? new Dictionary<string, string>()
                : sourceTexts.ToDictionary(x => x.Key, _ => "补全翻译")));

        using var concurrencyManager = new ConcurrencyManager(1);
        using var progressReporter = new ThreadSafeProgressReporter();
        using var service = new MultiThreadedTranslationService(llmService);

        var successfulBatches = await service.ExecuteBatchesAsync(
            batchResult,
            concurrencyManager,
            progressReporter,
            [Api("Primary", "https://primary.example"), Api("Secondary", "https://secondary.example")],
            "Chinese");

        successfulBatches.Should().Be(1);
        item.Status.Should().Be("已翻译");
        item.TranslatedText.Should().Be("补全翻译");
    }

    [Fact]
    public async Task ExecuteBatchesAsync_WhenAllApisFail_StopsAndReportsFailedApiNames()
    {
        var item = CreateItem("failure");
        var batchResult = CreateBatchResult(item);
        var llmService = new StubLlmService((_, _, _, _) =>
            throw new InvalidOperationException("API failure"));

        using var concurrencyManager = new ConcurrencyManager(1);
        using var progressReporter = new ThreadSafeProgressReporter();
        using var service = new MultiThreadedTranslationService(llmService);

        var action = () => service.ExecuteBatchesAsync(
            batchResult,
            concurrencyManager,
            progressReporter,
            [Api("Primary", "https://primary.example"), Api("Secondary", "https://secondary.example")],
            "Chinese");

        var exception = await FluentActions.Awaiting(action)
            .Should().ThrowAsync<TranslationApiFailoverException>();

        exception.Which.FailedApiNames.Should().Equal("Primary", "Secondary");
        item.Status.Should().Be("等待中");
    }

    private static TranslationApiProfile Api(string name, string apiUrl) => new()
    {
        Name = name,
        ApiUrl = apiUrl,
        ApiKey = "test-key",
        Models = [new TranslationApiModel { ModelId = "test-model" }]
    };

    private static TranslationItem CreateItem(string originalText) => new()
    {
        Key = originalText,
        OriginalText = originalText
    };

    private static BatchingService.BatchResult CreateBatchResult(params TranslationItem[] items)
    {
        var result = new BatchingService.BatchResult();
        foreach (var item in items)
        {
            var group = new[] { item }.GroupBy(x => x.OriginalText).Single();
            result.Batches.Add([group]);
            result.BatchTokenCounts.Add(1);
        }

        return result;
    }

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
