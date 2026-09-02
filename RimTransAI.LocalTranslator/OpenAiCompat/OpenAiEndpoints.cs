using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Diagnostics;
using RimTransAI.LocalTranslator.Translation;

namespace RimTransAI.LocalTranslator.OpenAiCompat;

public static class OpenAiEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, ServiceHealth health, BatchTranslator translator, TranslatorOptions options)
    {
        app.MapGet("/health", () =>
        {
            var snapshot = health.Current;
            var status = snapshot.Status.ToString().ToLowerInvariant();
            var response = new
            {
                status,
                model = options.ModelId,
                modelPath = snapshot.ModelPath ?? options.ResolvedModelPath,
                backend = "Cpu",
                error = snapshot.Status == TranslatorServiceStatus.Failed ? snapshot.Detail : null
            };
            return Results.Json(response, statusCode: snapshot.Status == TranslatorServiceStatus.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        app.MapGet("/v1/models", () =>
        {
            if (!health.IsReady) return Error(StatusCodes.Status503ServiceUnavailable, "模型尚未就绪。", "service_unavailable");
            return Results.Json(new
            {
                @object = "list",
                data = new[] { new { id = options.ModelId, @object = "model", owned_by = "local" } }
            });
        });

        app.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            var requestId = context.Response.Headers["X-Request-Id"].ToString();
            ChatCompletionRequest request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<ChatCompletionRequest>(
                    context.Request.Body,
                    SerializerOptions,
                    context.RequestAborted).ConfigureAwait(false)
                    ?? throw new JsonException("请求体为空。");
            }
            catch (JsonException)
            {
                return Error(StatusCodes.Status400BadRequest, "请求体不是有效的 JSON。", "invalid_request");
            }

            if (request.Stream == true)
                return Error(StatusCodes.Status400BadRequest, "本地翻译服务不支持流式响应。", "invalid_request");
            if (request.ResponseFormat?.Type is not null && !string.Equals(request.ResponseFormat.Type, "json_object", StringComparison.Ordinal))
                return Error(StatusCodes.Status400BadRequest, "仅支持 response_format.type=json_object。", "invalid_request");
            if (!health.IsReady)
                return Error(StatusCodes.Status503ServiceUnavailable, "模型尚未就绪。", "service_unavailable");

            IReadOnlyList<KeyValuePair<string, string>> entries;
            try
            {
                var userContent = request.Messages?
                    .LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                    ?.Content;
                entries = ParseEntries(userContent, options);
            }
            catch (InvalidRequestException exception)
            {
                return Error(StatusCodes.Status400BadRequest, exception.Message, "invalid_request");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            try
            {
                var translations = await translator.TranslateAsync(entries, requestId, timeout.Token).ConfigureAwait(false);
                var content = JsonSerializer.Serialize(translations, SerializerOptions);
                var response = new ChatCompletionResponse(
                    $"chatcmpl-local-{Guid.NewGuid():N}",
                    "chat.completion",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    request.Model ?? options.ModelId,
                    [new ChatCompletionChoice(0, new ChatCompletionMessage("assistant", content), "stop")],
                    new ChatCompletionUsage(0, 0, 0));
                return Results.Json(response, SerializerOptions);
            }
            catch (TranslationQueueFullException)
            {
                return Error(StatusCodes.Status429TooManyRequests, "翻译队列已满，请稍后重试。", "rate_limit_exceeded");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Error(499, "客户端已取消请求。", "request_cancelled");
            }
            catch (OperationCanceledException)
            {
                return Error(StatusCodes.Status504GatewayTimeout, "模型推理超时。", "model_timeout");
            }
        });
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseEntries(string? content, TranslatorOptions options)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidRequestException("缺少 user 消息中的 JSON 对象。");
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidRequestException("user 消息必须是 JSON 对象。");

            var entries = new List<KeyValuePair<string, string>>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidRequestException("JSON 对象中不能包含重复的键。");
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidRequestException("JSON 对象的值必须是字符串。");
                if (property.Value.GetString()!.Length > options.MaxInputCharactersPerItem)
                    throw new InvalidRequestException($"单条文本不能超过 {options.MaxInputCharactersPerItem} 个字符。");
                entries.Add(new KeyValuePair<string, string>(property.Name, property.Value.GetString()!));
            }

            if (entries.Count == 0) throw new InvalidRequestException("JSON 对象不能为空。");
            if (entries.Count > options.MaxItemsPerRequest)
                throw new InvalidRequestException($"每个请求最多包含 {options.MaxItemsPerRequest} 条文本。");
            return entries;
        }
        catch (JsonException)
        {
            throw new InvalidRequestException("user 消息不是有效的 JSON 对象。");
        }
    }

    private static IResult Error(int statusCode, string message, string code) =>
        Results.Json(new OpenAiError(new OpenAiErrorDetail(message, code, code)), statusCode: statusCode);

    private sealed class InvalidRequestException(string message) : Exception(message);
}
