using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class FileRegistry
{
    private readonly HashSet<string> _registry = new(StringComparer.OrdinalIgnoreCase);
    private int _attemptCount;
    private int _duplicateCount;

    public int AttemptCount => _attemptCount;

    public int RegisteredCount => _registry.Count;

    public int DuplicateCount => _duplicateCount;

    public bool TryRegister(string scope, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        _attemptCount++;
        var normalized = relativePath.Replace('\\', '/');
        var added = _registry.Add($"{scope}|{normalized}");
        if (!added)
        {
            _duplicateCount++;
        }

        return added;
    }

    public void Clear()
    {
        _registry.Clear();
        _attemptCount = 0;
        _duplicateCount = 0;
    }
}
