using System;
using Microsoft.Extensions.Logging;

namespace RimTransAI.Services;

/// <summary>
/// 为迁移后的 ILogger 提供安全的自由文本入口；关键业务事件应优先使用结构化模板。
/// </summary>
public static class LoggingExtensions
{
    public static void TraceMessage(this ILogger logger, string message) =>
        logger.LogTrace("{LogMessage}", message);

    public static void DebugMessage(this ILogger logger, string message) =>
        logger.LogDebug("{LogMessage}", message);

    public static void InfoMessage(this ILogger logger, string message) =>
        logger.LogInformation("{LogMessage}", message);

    public static void WarningMessage(this ILogger logger, string message) =>
        logger.LogWarning("{LogMessage}", message);

    public static void ErrorMessage(this ILogger logger, string message) =>
        logger.LogError("{LogMessage}", message);

    public static void ErrorMessage(this ILogger logger, string message, Exception exception) =>
        logger.LogError(exception, "{LogMessage}", message);
}
