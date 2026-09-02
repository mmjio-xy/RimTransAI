namespace RimTransAI.LocalTranslator.Configuration;

public sealed class TranslatorOptions
{
    public int Port { get; set; } = 18080;
    public string ModelPath { get; set; } = Path.Combine("Models", "Hy-MT2-1.8B-Q8_0.gguf");
    public string ModelId { get; set; } = "hy-mt2-1.8b";
    public int ContextSize { get; set; } = 4096;
    public int GpuLayerCount { get; set; }
    public int MaxTokensPerItem { get; set; } = 512;
    public int MaxItemsPerRequest { get; set; } = 50;
    public int MaxInputCharactersPerItem { get; set; } = 12000;
    public int MaxRequestBodyBytes { get; set; } = 1024 * 1024;
    public int MaxQueueLength { get; set; } = 20;
    public int RequestTimeoutSeconds { get; set; } = 120;
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    public string ResolvedModelPath => Path.IsPathRooted(ModelPath)
        ? ModelPath
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ModelPath));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Port is < 1 or > 65535) errors.Add("端口必须在 1 到 65535 之间。");
        if (string.IsNullOrWhiteSpace(ModelPath)) errors.Add("请选择模型文件。");
        else if (!File.Exists(ResolvedModelPath)) errors.Add($"找不到模型文件：{ResolvedModelPath}");
        if (ContextSize < 512) errors.Add("上下文长度不能小于 512。");
        if (MaxTokensPerItem is < 1 or > 4096) errors.Add("单条最大生成 token 必须在 1 到 4096 之间。");
        if (MaxItemsPerRequest is < 1 or > 500) errors.Add("每批条目数必须在 1 到 500 之间。");
        if (MaxInputCharactersPerItem < 1) errors.Add("单条输入字符限制必须大于 0。");
        if (MaxQueueLength < 1) errors.Add("队列长度必须大于 0。");
        if (RequestTimeoutSeconds < 5) errors.Add("请求超时不能小于 5 秒。");

        return errors;
    }

    public TranslatorOptions Clone() => (TranslatorOptions)MemberwiseClone();
}
