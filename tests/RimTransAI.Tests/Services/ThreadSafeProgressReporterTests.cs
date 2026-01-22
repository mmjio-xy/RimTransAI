using System.Threading.Tasks;
using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ThreadSafeProgressReporterTests
{
    [Fact]
    public void Constructor_WithNullCallbacks_DoesNotThrow()
    {
        // Arrange & Act
        var reporter = new ThreadSafeProgressReporter(null, null);

        // Assert
        reporter.Should().NotBeNull();
        reporter.Dispose();
    }

    [Fact]
    public void Constructor_WithValidCallbacks_CreatesInstance()
    {
        // Arrange
        Action<TranslationProgress>? progressCallback = _ => { };
        Action<string>? logCallback = _ => { };

        // Act
        var reporter = new ThreadSafeProgressReporter(progressCallback, logCallback);

        // Assert
        reporter.Should().NotBeNull();

        // Cleanup
        reporter.Dispose();
    }

    [Fact]
    public void ReportProgress_WithValidParameters_CallsProgressCallback()
    {
        // Arrange
        TranslationProgress? capturedProgress = null;
        var reporter = new ThreadSafeProgressReporter(
            progress => capturedProgress = progress,
            null);

        // Act
        reporter.ReportProgress(5, 10, 3, "Test batch");

        // Assert
        capturedProgress.Should().NotBeNull();
        capturedProgress!.ProcessedBatches.Should().Be(5);
        capturedProgress.TotalBatches.Should().Be(10);
        capturedProgress.ActiveThreads.Should().Be(3);
        capturedProgress.CurrentBatchInfo.Should().Be("Test batch");

        reporter.Dispose();
    }

    [Fact]
    public void ReportProgress_AfterDisposal_DoesNotCallCallback()
    {
        // Arrange
        var callbackCalled = false;
        var reporter = new ThreadSafeProgressReporter(
            _ => callbackCalled = true,
            null);
        reporter.Dispose();

        // Act
        reporter.ReportProgress(5, 10, 3);

        // Assert
        callbackCalled.Should().BeFalse();
    }

    [Fact]
    public void ReportLog_WithString_CallsLogCallback()
    {
        // Arrange
        string? capturedLog = null;
        var reporter = new ThreadSafeProgressReporter(
            null,
            log => capturedLog = log);

        // Act
        reporter.ReportLog("Test log message");

        // Assert
        capturedLog.Should().Be("Test log message");

        reporter.Dispose();
    }

    [Fact]
    public void ReportLog_WithFormatString_CallsLogCallback()
    {
        // Arrange
        string? capturedLog = null;
        var reporter = new ThreadSafeProgressReporter(
            null,
            log => capturedLog = log);

        // Act
        reporter.ReportLog("Processing batch {0} of {1}", 5, 10);

        // Assert
        capturedLog.Should().Be("Processing batch 5 of 10");

        reporter.Dispose();
    }

    [Fact]
    public void ReportLog_AfterDisposal_DoesNotCallCallback()
    {
        // Arrange
        var callbackCalled = false;
        var reporter = new ThreadSafeProgressReporter(
            null,
            _ => callbackCalled = true);
        reporter.Dispose();

        // Act
        reporter.ReportLog("Test message");

        // Assert
        callbackCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ReportProgress_ThreadSafe_MultipleThreadsNoException()
    {
        // Arrange
        var progressCount = 0;
        var lockObj = new object();

        var reporter = new ThreadSafeProgressReporter(
            _ =>
            {
                lock (lockObj)
                {
                    progressCount++;
                }
            },
            _ =>
            {
                lock (lockObj)
                {
                    progressCount++;
                }
            });

        // Act - 启动多个线程同时报告
        var tasks = new Task[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    reporter.ReportProgress(j, 100, 5);
                    reporter.ReportLog($"Thread {i}, Iteration {j}");
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        progressCount.Should().Be(2000); // 10 threads * 100 iterations * 2 callbacks

        reporter.Dispose();
    }
}
