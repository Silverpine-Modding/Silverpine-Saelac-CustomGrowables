#nullable enable
using System;
using System.Globalization;

namespace SilverpineMods.CustomGrowables;

internal static class EditorTextLayout
{
    internal const float SidebarWidth = 420f;
    internal const float ItemRowHeight = 76f;

    // Fit the actual font metrics, not a character-count approximation. Never
    // split surrogate pairs or combining characters when adding the ellipsis.
    internal static string FitSingleLine(string? value, float width, Func<string, float> measure)
    {
        string text = (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (string.IsNullOrEmpty(text) || width <= 0 || float.IsNaN(width)) return "";
        if (measure(text) <= width) return text;
        const string ellipsis = "…";
        if (measure(ellipsis) > width) return "";
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = starts.Length;
        while (low < high)
        {
            int count = (low + high + 1) / 2;
            int end = count == starts.Length ? text.Length : starts[count];
            if (measure(text.Substring(0, end) + ellipsis) <= width) low = count;
            else high = count - 1;
        }
        return text.Substring(0, low == starts.Length ? text.Length : starts[low]) + ellipsis;
    }
}
