using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class ModInfoPanel : UserControl
{
    private ModInfoViewModel? _modInfoVm;

    public ModInfoPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachFromModInfoVm();
        _modInfoVm = DataContext as ModInfoViewModel;
        AttachToModInfoVm();
        RenderRichDescription(_modInfoVm?.Description);
    }

    private void AttachToModInfoVm()
    {
        if (_modInfoVm != null)
        {
            _modInfoVm.PropertyChanged += OnModInfoVmPropertyChanged;
        }
    }

    private void DetachFromModInfoVm()
    {
        if (_modInfoVm != null)
        {
            _modInfoVm.PropertyChanged -= OnModInfoVmPropertyChanged;
            _modInfoVm = null;
        }
    }

    private void OnModInfoVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ModInfoViewModel.Description))
        {
            RenderRichDescription(_modInfoVm?.Description);
        }
    }

    private void RenderRichDescription(string? source)
    {
        if (ModDescriptionTextBlock.Inlines == null)
        {
            return;
        }

        ModDescriptionTextBlock.Inlines.Clear();

        var lines = ParseRichLines(source);
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var segment in lines[i])
            {
                var run = new Run(segment.Text)
                {
                    FontWeight = segment.IsBold ? FontWeight.Bold : FontWeight.Normal
                };

                if (segment.Foreground != null)
                {
                    run.Foreground = segment.Foreground;
                }

                ModDescriptionTextBlock.Inlines.Add(run);
            }

            if (i < lines.Count - 1)
            {
                ModDescriptionTextBlock.Inlines.Add(new LineBreak());
            }
        }
    }

    private static List<List<RichSegment>> ParseRichLines(string? source)
    {
        var decoded = WebUtility.HtmlDecode(source ?? string.Empty)
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var rawLines = decoded.Split('\n');
        var lines = new List<List<RichSegment>>(rawLines.Length);
        foreach (var line in rawLines)
        {
            lines.Add(ParseRichLine(line));
        }

        return lines;
    }

    private static List<RichSegment> ParseRichLine(string line)
    {
        var segments = new List<RichSegment>();
        if (string.IsNullOrEmpty(line))
        {
            return segments;
        }

        var sb = new StringBuilder();
        var bold = false;
        IBrush? currentForeground = null;
        var colorStack = new Stack<IBrush?>();

        void Flush()
        {
            if (sb.Length == 0)
            {
                return;
            }

            segments.Add(new RichSegment(sb.ToString(), bold, currentForeground));
            sb.Clear();
        }

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '<')
            {
                sb.Append(line[i]);
                continue;
            }

            var closeIndex = line.IndexOf('>', i + 1);
            if (closeIndex <= i)
            {
                sb.Append(line[i]);
                continue;
            }

            var tag = line.Substring(i + 1, closeIndex - i - 1).Trim();
            if (IsSupportedTag(tag))
            {
                Flush();
                TryApplyTag(tag, ref bold, ref currentForeground, colorStack);
                i = closeIndex;
                continue;
            }

            sb.Append(line[i]);
        }

        Flush();
        return segments;
    }

    private static bool TryApplyTag(string tag, ref bool bold, ref IBrush? currentForeground, Stack<IBrush?> colorStack)
    {
        if (tag.Equals("b", StringComparison.OrdinalIgnoreCase))
        {
            bold = true;
            return true;
        }

        if (tag.Equals("/b", StringComparison.OrdinalIgnoreCase))
        {
            bold = false;
            return true;
        }

        if (tag.StartsWith("color=", StringComparison.OrdinalIgnoreCase))
        {
            var rawColor = tag.Substring("color=".Length).Trim().Trim('"', '\'');
            colorStack.Push(currentForeground);
            if (Color.TryParse(rawColor, out var color))
            {
                currentForeground = new SolidColorBrush(color);
            }
            else
            {
                currentForeground = null;
            }

            return true;
        }

        if (tag.Equals("/color", StringComparison.OrdinalIgnoreCase))
        {
            currentForeground = colorStack.Count > 0 ? colorStack.Pop() : null;
            return true;
        }

        return false;
    }

    private static bool IsSupportedTag(string tag)
    {
        if (tag.Equals("b", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("/b", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("/color", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return tag.StartsWith("color=", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RichSegment(string Text, bool IsBold, IBrush? Foreground);
}
