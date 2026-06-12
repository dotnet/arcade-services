// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;

namespace ProductConstructionService.BarViz.Code.Helpers;

public static class StringExtensions
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

    /// <summary>
    /// A segment of a message: either plain text or a URL.
    /// </summary>
    public readonly record struct TextSegment(string Text, bool IsUrl);

    /// <summary>
    /// Splits <paramref name="text"/> into consecutive segments of plain text and URLs so the
    /// URLs can be rendered as links while the rest stays as text. Trailing punctuation that is
    /// unlikely to be part of a URL (e.g. a sentence-ending '.', ')' or ',') is excluded from
    /// the link and kept as text.
    /// </summary>
    public static IReadOnlyList<TextSegment> SplitIntoTextAndUrls(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = new List<TextSegment>();
        var lastIndex = 0;

        foreach (Match match in UrlRegex.Matches(text))
        {
            var url = match.Value;
            var trimmedLength = url.Length;
            while (trimmedLength > 0 && IsTrailingPunctuation(url[trimmedLength - 1]))
            {
                trimmedLength--;
            }

            if (match.Index > lastIndex)
            {
                segments.Add(new TextSegment(text[lastIndex..match.Index], IsUrl: false));
            }

            segments.Add(new TextSegment(url[..trimmedLength], IsUrl: true));

            if (trimmedLength < url.Length)
            {
                segments.Add(new TextSegment(url[trimmedLength..], IsUrl: false));
            }

            lastIndex = match.Index + url.Length;
        }

        if (lastIndex < text.Length)
        {
            segments.Add(new TextSegment(text[lastIndex..], IsUrl: false));
        }

        return segments;
    }

    private static bool IsTrailingPunctuation(char c)
        => c is '.' or ',' or ';' or ':' or ')' or ']' or '}' or '!' or '?' or '"' or '\'';

    public static string[] ParseSearchTerms(string searchFilter)
    {
        var terms = new List<string>();
        var currentTerm = new StringBuilder();
        bool inQuotes = false;

        foreach (var c in searchFilter)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                if (!inQuotes && currentTerm.Length > 0)
                {
                    terms.Add(currentTerm.ToString());
                    currentTerm.Clear();
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (currentTerm.Length > 0)
                {
                    terms.Add(currentTerm.ToString());
                    currentTerm.Clear();
                }
            }
            else
            {
                currentTerm.Append(c);
            }
        }

        if (currentTerm.Length > 0)
        {
            terms.Add(currentTerm.ToString());
        }

        return [.. terms];
    }
}
