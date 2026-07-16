using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using RimTransAI.Models;

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

    public static void LogUserInformation(
        this ILogger logger,
        string messageTemplate,
        params object?[] args) =>
        LogUserEvent(logger, LogLevel.Information, OperationLogLevel.Info, null, messageTemplate, args);

    public static void LogUserSuccess(
        this ILogger logger,
        string messageTemplate,
        params object?[] args) =>
        LogUserEvent(logger, LogLevel.Information, OperationLogLevel.Success, null, messageTemplate, args);

    public static void LogUserWarning(
        this ILogger logger,
        string messageTemplate,
        params object?[] args) =>
        LogUserEvent(logger, LogLevel.Warning, OperationLogLevel.Warning, null, messageTemplate, args);

    public static void LogUserWarning(
        this ILogger logger,
        Exception? exception,
        string messageTemplate,
        params object?[] args) =>
        LogUserEvent(logger, LogLevel.Warning, OperationLogLevel.Warning, exception, messageTemplate, args);

    public static void LogUserError(
        this ILogger logger,
        Exception? exception,
        string messageTemplate,
        params object?[] args) =>
        LogUserEvent(logger, LogLevel.Error, OperationLogLevel.Error, exception, messageTemplate, args);

    private static void LogUserEvent(
        ILogger logger,
        LogLevel logLevel,
        OperationLogLevel uiLevel,
        Exception? exception,
        string messageTemplate,
        object?[] args)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [OperationLogSink.UiVisibleProperty] = true,
            [OperationLogSink.UiLevelProperty] = uiLevel.ToString()
        });

        logger.Log(logLevel, exception, messageTemplate, args);
    }
}
