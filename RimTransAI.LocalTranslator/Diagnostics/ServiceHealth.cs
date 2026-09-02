namespace RimTransAI.LocalTranslator.Diagnostics;

public enum TranslatorServiceStatus
{
    Stopped,
    Loading,
    Ready,
    Failed
}

public sealed record ServiceHealthSnapshot(
    TranslatorServiceStatus Status,
    string? Detail,
    string? ModelPath,
    DateTimeOffset ChangedAt);

public sealed class ServiceHealth
{
    private readonly object _gate = new();
    private ServiceHealthSnapshot _current = new(TranslatorServiceStatus.Stopped, null, null, DateTimeOffset.Now);

    public event EventHandler<ServiceHealthSnapshot>? Changed;

    public ServiceHealthSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public bool IsReady => Current.Status == TranslatorServiceStatus.Ready;

    public void Set(TranslatorServiceStatus status, string? detail = null, string? modelPath = null)
    {
        ServiceHealthSnapshot snapshot;
        lock (_gate)
        {
            snapshot = new ServiceHealthSnapshot(status, detail, modelPath, DateTimeOffset.Now);
            _current = snapshot;
        }

        Changed?.Invoke(this, snapshot);
    }
}
