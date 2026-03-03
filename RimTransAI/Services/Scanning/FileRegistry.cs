using System;
using System.Collections.Generic;

namespace RimTransAI.Services.Scanning;

public sealed class FileRegistry
{
    private readonly HashSet<string> _registry = new(StringComparer.OrdinalIgnoreCase);

    public bool TryRegister(string scope, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalized = relativePath.Replace('\\', '/');
        return _registry.Add($"{scope}|{normalized}");
    }

    public void Clear()
    {
        _registry.Clear();
    }
}
