using FluentAssertions;
using Microsoft.Extensions.Logging;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class LoggingExtensionsTests
{
    [Fact]
    public void FreeTextExtensions_PreserveLevelsMessagesAndExceptions()
    {
        var logger = new RecordingLogger<LoggingExtensionsTests>();
        var exception = new InvalidOperationException("failure");

        logger.TraceMessage("trace");
        logger.DebugMessage("debug");
        logger.InfoMessage("info");
        logger.WarningMessage("warning");
        logger.ErrorMessage("error", exception);
        logger.LogUserSuccess("saved {Count}", 3);

        logger.Records.Select(x => x.Level).Should().Equal(
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Information,
            LogLevel.Warning,
            LogLevel.Error,
            LogLevel.Information);
        logger.Records.Select(x => x.Message).Should().Equal(
            "trace",
            "debug",
            "info",
            "warning",
            "error",
            "saved 3");
        logger.Records.ElementAt(4).Exception.Should().BeSameAs(exception);
    }
}
