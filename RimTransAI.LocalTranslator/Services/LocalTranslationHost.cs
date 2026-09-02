using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Diagnostics;
using RimTransAI.LocalTranslator.Engine;
using RimTransAI.LocalTranslator.OpenAiCompat;
using RimTransAI.LocalTranslator.Translation;

namespace RimTransAI.LocalTranslator.Services;

public sealed class LocalTranslationHost : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LocalTranslationHost> _logger;
    private readonly ServiceHealth _health;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WebApplication? _application;
    private ITranslationEngine? _engine;
    private BatchTranslator? _translator;
    private CancellationTokenSource? _modelLoadCancellation;
    private Task? _modelLoadTask;

    public LocalTranslationHost(ILoggerFactory loggerFactory, ServiceHealth health)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<LocalTranslationHost>();
        _health = health;
    }

    public ServiceHealth Health => _health;

    public bool IsRunning => _application is not null;

    public async Task StartAsync(TranslatorOptions settings, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_application is not null) return;

            if (settings.Port is < 1 or > 65535)
                throw new InvalidOperationException("端口必须在 1 到 65535 之间。");

            var options = settings.Clone();
            _health.Set(TranslatorServiceStatus.Loading, "正在启动本地服务...", options.ResolvedModelPath);
            var webBuilder = WebApplication.CreateSlimBuilder();
            webBuilder.Logging.ClearProviders();
            webBuilder.Logging.AddProvider(new ForwardingLoggerProvider(_loggerFactory));
            webBuilder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.ListenLocalhost(options.Port);
                kestrel.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes;
            });

            var app = webBuilder.Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Request-Id"] = Guid.NewGuid().ToString("N");
                await next(context).ConfigureAwait(false);
            });
            app.UseExceptionHandler(errorApplication => errorApplication.Run(context =>
                Results.Json(
                    new OpenAiError(new OpenAiErrorDetail("服务内部错误。", "internal_error", "internal_error")),
                    statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context)));

            var engine = new LlamaSharpTranslationEngine(_loggerFactory.CreateLogger<LlamaSharpTranslationEngine>());
            var translator = new BatchTranslator(engine, options, _loggerFactory.CreateLogger<BatchTranslator>());
            OpenAiEndpoints.Map(app, _health, translator, options);

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                translator.Dispose();
                engine.Dispose();
                _health.Set(TranslatorServiceStatus.Failed, $"端口 {options.Port} 无法启动。", options.ResolvedModelPath);
                throw;
            }

            _application = app;
            _engine = engine;
            _translator = translator;
            _modelLoadCancellation = new CancellationTokenSource();
            _modelLoadTask = LoadModelAsync(engine, options, _modelLoadCancellation.Token);
            _logger.LogInformation("本地翻译 HTTP 服务已启动 Endpoint=http://127.0.0.1:{Port}", options.Port);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _modelLoadCancellation?.Cancel();
            if (_modelLoadTask is not null)
            {
                try { await _modelLoadTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            if (_application is not null)
            {
                _logger.LogInformation("正在停止本地翻译服务");
                await _application.StopAsync(cancellationToken).ConfigureAwait(false);
                await _application.DisposeAsync().ConfigureAwait(false);
            }

            _translator?.Dispose();
            _engine?.Dispose();
            _modelLoadCancellation?.Dispose();
            _application = null;
            _engine = null;
            _translator = null;
            _modelLoadCancellation = null;
            _modelLoadTask = null;
            _health.Set(TranslatorServiceStatus.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task LoadModelAsync(ITranslationEngine engine, TranslatorOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var validationErrors = options.Validate();
            if (validationErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", validationErrors));

            await engine.LoadAsync(options, cancellationToken).ConfigureAwait(false);
            _health.Set(TranslatorServiceStatus.Ready, "模型已就绪。", options.ResolvedModelPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("模型加载已取消");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "本地翻译模型加载失败");
            _health.Set(TranslatorServiceStatus.Failed, exception.Message, options.ResolvedModelPath);
        }
    }

    // Kestrel uses its own logging factory; forward it to the application's logger factory.
    private sealed class ForwardingLoggerProvider(ILoggerFactory factory) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => factory.CreateLogger(categoryName);
        public void Dispose() { }
    }
}
