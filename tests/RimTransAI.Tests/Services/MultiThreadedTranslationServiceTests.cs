using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class MultiThreadedTranslationServiceTests
{
    [Fact]
    public async Task ExecuteBatchesAsync_WhenOneBatchFails_ReturnsOnlySuccessfulBatchCount()
    {
        var successfulItem = CreateItem("good");
        var failedItem = CreateItem("bad");
        var batchResult = CreateBatchResult(successfulItem, failedItem);
        var llmService = new StubLlmService((sourceTexts, _) =>
        {
            if (sourceTexts.ContainsKey("bad"))
            {
                throw new InvalidOperationException("API failure");
            }

            return Task.FromResult(sourceTexts.ToDictionary(x => x.Key, _ => "翻译成功"));
        });
        var dispatchedUpdates = 0;
        var logger = new RecordingLogger<MultiThreadedTranslationService>();

        using var concurrencyManager = new ConcurrencyManager(2);
        using var progressReporter = new ThreadSafeProgressReporter();
        using var service = new MultiThreadedTranslationService(
            llmService,
            update =>
            {
                Interlocked.Increment(ref dispatchedUpdates);
                update();
                return Task.CompletedTask;
            },
            logger);

        var successfulBatches = await service.ExecuteBatchesAsync(
            batchResult,
            concurrencyManager,
            progressReporter,
            "api-key",
            "https://example.com",
            "model",
            "Chinese",
            480);

        successfulBatches.Should().Be(1);
        successfulItem.Status.Should().Be("已翻译");
        successfulItem.TranslatedText.Should().Be("翻译成功");
        failedItem.Status.Should().Be("翻译失败");
        failedItem.TranslatedText.Should().BeEmpty();
        dispatchedUpdates.Should().Be(2);
        logger.Records.Any(record =>
                record.Exception is InvalidOperationException &&
                record.Properties.TryGetValue("BatchIndex", out var batchIndex) &&
                Equals(batchIndex, 2))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBatchesAsync_WhenRequestIsCancelled_PropagatesCancellation()
    {
        var item = CreateItem("cancel-me");
        var batchResult = CreateBatchResult(item);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var llmService = new StubLlmService(async (_, cancellationToken) =>
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
            "api-key",
            "https://example.com",
            "model",
            "Chinese",
            480,
            cancellationToken: cts.Token);

        await requestStarted.Task;
        cts.Cancel();

        await FluentActions.Awaiting(() => execution)
            .Should().ThrowAsync<OperationCanceledException>();
        item.Status.Should().Be("等待中");
    }

    private static TranslationItem CreateItem(string originalText)
    {
        return new TranslationItem
        {
            Key = originalText,
            OriginalText = originalText
        };
    }

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
        Func<Dictionary<string, string>, CancellationToken, Task<Dictionary<string, string>>> handler)
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
            return handler(sourceTexts, cancellationToken);
        }
    }
}
