using System;
using System.Collections.Generic;
using System.Linq;

namespace RimTransAI.Services.Scanning;

public sealed class DefPathBuilder
{
    public string BuildKey(string defName, IEnumerable<string> pathSegments)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);

        var normalizedDefName = NormalizeSegment(defName);
        var normalizedPath = BuildRelativePath(pathSegments);

        if (string.IsNullOrWhiteSpace(normalizedDefName))
        {
            return normalizedPath;
        }

        return string.IsNullOrWhiteSpace(normalizedPath)
            ? normalizedDefName
            : $"{normalizedDefName}.{normalizedPath}";
    }

    public string BuildRelativePath(IEnumerable<string> pathSegments)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);

        var segments = pathSegments
            .Select(NormalizeSegment)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join('.', segments);
    }

    public string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalized = key
            .Replace("[", ".", StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Trim();

        var parts = normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSegment)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join('.', parts);
    }

    private static string NormalizeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        return segment.Trim().Replace(' ', '_');
    }
}
