using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ConcurrencyManagerTests
{
    [Fact]
    public async Task ExecuteAsync_WithRequestInterval_SpacesRequestStartTimes()
    {
        const int intervalMs = 80;
        using var manager = new ConcurrencyManager(maxConcurrentRequests: 3, intervalMs);
        var stopwatch = Stopwatch.StartNew();
        var requestStartTimes = new ConcurrentQueue<TimeSpan>();

        var operations = Enumerable.Range(0, 3)
            .Select(_ => manager.ExecuteAsync(async cancellationToken =>
            {
                requestStartTimes.Enqueue(stopwatch.Elapsed);
                await Task.Delay(5, cancellationToken);
                return true;
            }));

        await Task.WhenAll(operations);

        var starts = requestStartTimes.ToArray();
        starts.Should().HaveCount(3);
        (starts[1] - starts[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60));
        (starts[2] - starts[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledWhileWaiting_DoesNotInvokeOperation()
    {
        using var manager = new ConcurrencyManager(maxConcurrentRequests: 1);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequestInvoked = false;

        var firstRequest = manager.ExecuteAsync(async _ =>
        {
            firstRequestStarted.SetResult();
            await releaseFirstRequest.Task;
            return true;
        });
        await firstRequestStarted.Task;

        using var cts = new CancellationTokenSource();
        var secondRequest = manager.ExecuteAsync(_ =>
        {
            secondRequestInvoked = true;
            return Task.FromResult(true);
        }, cts.Token);

        cts.Cancel();

        await FluentActions.Awaiting(() => secondRequest)
            .Should().ThrowAsync<OperationCanceledException>();
        secondRequestInvoked.Should().BeFalse();

        releaseFirstRequest.SetResult();
        await firstRequest;
    }
}
