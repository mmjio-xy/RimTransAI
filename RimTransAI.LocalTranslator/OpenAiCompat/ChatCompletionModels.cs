namespace RimTransAI.LocalTranslator.OpenAiCompat;

public sealed record ChatCompletionRequest(
    string? Model,
    IReadOnlyList<ChatMessage>? Messages,
    float? Temperature,
    ResponseFormat? ResponseFormat,
    bool? Stream);

public sealed record ChatMessage(string? Role, string? Content);

public sealed record ResponseFormat(string? Type);

public sealed record OpenAiError(OpenAiErrorDetail Error);

public sealed record OpenAiErrorDetail(string Message, string Type, string Code);

public sealed record ChatCompletionResponse(
    string Id,
    string Object,
    long Created,
    string Model,
    IReadOnlyList<ChatCompletionChoice> Choices,
    ChatCompletionUsage Usage);

public sealed record ChatCompletionChoice(int Index, ChatCompletionMessage Message, string FinishReason);

public sealed record ChatCompletionMessage(string Role, string Content);

public sealed record ChatCompletionUsage(int PromptTokens, int CompletionTokens, int TotalTokens);
