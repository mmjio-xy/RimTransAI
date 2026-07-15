using System;
using System.Collections.ObjectModel;
using RimTransAI.Models;

namespace RimTransAI.Services;

public sealed class OperationLogBuffer
{
    private readonly ObservableCollection<OperationLogEntry> _entries = new();

    public OperationLogBuffer(int capacity = 500)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        Entries = new ReadOnlyObservableCollection<OperationLogEntry>(_entries);
    }

    public int Capacity { get; }
    public ReadOnlyObservableCollection<OperationLogEntry> Entries { get; }
    public string LatestMessage => _entries.Count == 0 ? string.Empty : _entries[^1].Message;

    public void Replace(string? text)
    {
        _entries.Clear();
        Append(text);
    }

    public void Append(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            _entries.Add(OperationLogEntry.Create(line));
            while (_entries.Count > Capacity)
            {
                _entries.RemoveAt(0);
            }
        }
    }
}
