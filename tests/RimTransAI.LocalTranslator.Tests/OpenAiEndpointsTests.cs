using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Diagnostics;
using RimTransAI.LocalTranslator.Engine;
using RimTransAI.LocalTranslator.OpenAiCompat;
using RimTransAI.LocalTranslator.Translation;
using Xunit;

namespace RimTransAI.LocalTranslator.Tests;

public sealed class OpenAiEndpointsTests
{
    [Fact]
    public async Task ChatCompletion_ReadyService_ReturnsOpenAiJsonWithTranslatedObject()
    {
        await using var server = await TestServer.StartAsync(ready: true);
        using var request = JsonContent.Create(new
        {
            model = "caller-model",
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = "this is ignored" },
                new { role = "user", content = "{\"Hello\":\"Hello\",\"World\":\"World\"}" }
            }
        });

        var response = await server.Client.PostAsync("/v1/chat/completions", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Request-Id"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("caller-model", body.RootElement.GetProperty("model").GetString());
        var content = body.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        using var translations = JsonDocument.Parse(content!);
        Assert.Equal("中:Hello", translations.RootElement.GetProperty("Hello").GetString());
        Assert.Equal("中:World", translations.RootElement.GetProperty("World").GetString());
    }

    [Fact]
    public async Task ChatCompletion_WhenNotReady_ReturnsServiceUnavailable()
    {
        await using var server = await TestServer.StartAsync(ready: false);
        using var request = JsonContent.Create(new
        {
            messages = new[] { new { role = "user", content = "{\"Hello\":\"Hello\"}" } }
        });

        var response = await server.Client.PostAsync("/v1/chat/completions", request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("service_unavailable", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChatCompletion_DuplicateKeys_ReturnsBadRequest()
    {
        await using var server = await TestServer.StartAsync(ready: true);
        const string request = """{"messages":[{"role":"user","content":"{\"same\":\"one\",\"same\":\"two\"}"}]}""";

        var response = await server.Client.PostAsync("/v1/chat/completions", new StringContent(request, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChatCompletion_InvalidOuterJson_ReturnsOpenAiError()
    {
        await using var server = await TestServer.StartAsync(ready: true);

        var response = await server.Client.PostAsync("/v1/chat/completions", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly BatchTranslator _translator;

        private TestServer(WebApplication application, BatchTranslator translator, HttpClient client)
        {
            _application = application;
            _translator = translator;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TestServer> StartAsync(bool ready)
        {
            var port = GetUnusedPort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var application = builder.Build();
            application.Use(async (context, next) =>
            {
                context.Response.Headers["X-Request-Id"] = "test-request";
                await next(context);
            });

            var options = new TranslatorOptions();
            var health = new ServiceHealth();
            if (ready) health.Set(TranslatorServiceStatus.Ready);
            var translator = new BatchTranslator(new PrefixingEngine(), options, NullLogger<BatchTranslator>.Instance);
            OpenAiEndpoints.Map(application, health, translator, options);
            await application.StartAsync();
            return new TestServer(application, translator, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync();
            await _application.DisposeAsync();
            _translator.Dispose();
        }

        private static int GetUnusedPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class PrefixingEngine : ITranslationEngine
    {
        public bool IsLoaded => true;
        public Task LoadAsync(TranslatorOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> TranslateAsync(string sourceText, CancellationToken cancellationToken) => Task.FromResult($"中:{sourceText}");
        public void Dispose() { }
    }
}
