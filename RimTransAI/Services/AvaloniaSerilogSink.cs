using System;
using System.Globalization;
using Avalonia.Logging;
using Serilog;

namespace RimTransAI.Services;

public sealed class AvaloniaSerilogSink : ILogSink
{
    private readonly Serilog.ILogger _logger;

    public AvaloniaSerilogSink()
        : this(Serilog.Log.Logger)
    {
    }

    public AvaloniaSerilogSink(Serilog.ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled(LogEventLevel level, string area)
    {
        return _logger.IsEnabled(MapLevel(level));
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Write(level, area, source, messageTemplate);
    }

    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        params object?[] propertyValues)
    {
        var message = FormatMessage(messageTemplate, propertyValues);
        Write(level, area, source, message);
    }

    private void Write(LogEventLevel level, string area, object? source, string message)
    {
        _logger.ForContext("SourceContext", $"Avalonia.{area}")
            .ForContext("AvaloniaSource", source?.GetType().FullName)
            .Write(MapLevel(level), "{AvaloniaMessage}", message);
    }

    private static string FormatMessage(string messageTemplate, object?[] propertyValues)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, messageTemplate, propertyValues);
        }
        catch (FormatException)
        {
            return $"{messageTemplate} | {string.Join(", ", propertyValues)}";
        }
    }

    private static Serilog.Events.LogEventLevel MapLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => Serilog.Events.LogEventLevel.Verbose,
            LogEventLevel.Debug => Serilog.Events.LogEventLevel.Debug,
            LogEventLevel.Information => Serilog.Events.LogEventLevel.Information,
            LogEventLevel.Warning => Serilog.Events.LogEventLevel.Warning,
            LogEventLevel.Error => Serilog.Events.LogEventLevel.Error,
            LogEventLevel.Fatal => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Debug
        };
    }
}
