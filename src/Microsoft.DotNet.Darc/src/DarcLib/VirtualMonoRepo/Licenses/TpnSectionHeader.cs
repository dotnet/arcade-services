// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;

public class TpnSectionHeader
{
    private static readonly char[] SectionSeparatorChars = { '-', '=' };
    private static readonly Regex NumberListPrefix = new(@"^[0-9]+\.\t(?<name>.*)$");

    public string Name { get; set; }
    public string SeparatorLine { get; set; }
    public TpnSectionHeaderFormat Format { get; set; }
    public int StartLine { get; set; }
    public int LineLength { get; set; }

    public TpnSectionHeader(
        string name,
        string separatorLine,
        TpnSectionHeaderFormat format,
        int startLine,
        int lineLength)
    {
        Name = name;
        SeparatorLine = separatorLine;
        Format = format;
        StartLine = startLine;
        LineLength = lineLength;
    }

    public string SingleLineName => Name.Replace('\n', ' ').Replace('\r', ' ');

    public override string ToString() => Format switch
    {
        TpnSectionHeaderFormat.Separated => SeparatorLine + TpnDocument.LineSeparator + TpnDocument.LineSeparator + Name,
        TpnSectionHeaderFormat.Underlined => Name + TpnDocument.LineSeparator + SeparatorLine,
        TpnSectionHeaderFormat.Numbered => SeparatorLine,
        _ => throw new ArgumentOutOfRangeException(),
    };

    private static TpnSectionHeader? ParseSeparated(string[] lines, int i)
    {
        var nameLines = lines
            .Skip(i + 2)
            .Reverse()
            .TakeWhile(s => !string.IsNullOrWhiteSpace(s))
            .Reverse()
            .Select(s => s.Trim())
            .ToArray();

        var name = string.Join(TpnDocument.LineSeparator, nameLines);

        // If there's a separator line as the last line in the name, this line doesn't indicate
        // a section. It needs to be handled by ParseUnderlined instead.
        if (!nameLines.Any(IsSeparatorLine))
        {
            return new TpnSectionHeader(
                name,
                lines[i],
                TpnSectionHeaderFormat.Separated,
                i,
                2 + nameLines.Length);
        }
        
        if (nameLines.Take(nameLines.Length - 1).Any(IsSeparatorLine))
        {
            throw new ArgumentException(
                $"Separator line detected inside name '{name}'");
        }

        return null;
    }

    public static bool IsSeparatorLine(string line)
    {
        return line.Length > 2 && line.All(c => SectionSeparatorChars.Contains(c));
    }

    public static IEnumerable<TpnSectionHeader> ParseAll(string[] lines)
    {
        // A separator line can't represent a section if it's on the first or last few lines.
        for (var i = 1; i < lines.Length - 2; i++)
        {
            var lineAbove = lines[i - 1].Trim();
            var line = lines[i].Trim();
            var lineBelow = lines[i + 1].Trim();

            if (line.Length > 2 && IsSeparatorLine(line) && string.IsNullOrEmpty(lineBelow))
            {
                // 'line' is a separator line. Check around to see what kind it is.
                if (string.IsNullOrEmpty(lineAbove))
                {
                    var header = ParseSeparated(lines, i);
                    if (header != null)
                    {
                        yield return header;
                    }
                }
                else
                {
                    var header = ParseUnderlined(lines, i);
                    yield return header;
                }
            }

            var numberedHeader = ParseNumberedOrNull(lines, i);
            if (numberedHeader != null)
            {
                yield return numberedHeader;
            }
        }
    }

    private static TpnSectionHeader ParseUnderlined(string[] lines, int i)
    {
        var nameLines = lines
            .Take(i)
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Select(s => s.Trim())
            .ToArray();

        return new TpnSectionHeader(
            string.Join(TpnDocument.LineSeparator, nameLines),
            lines[i],
            TpnSectionHeaderFormat.Underlined,
            i - nameLines.Length,
            nameLines.Length + 1);
    }
    
    private static TpnSectionHeader? ParseNumberedOrNull(string[] lines, int i)
    {
        if (!string.IsNullOrWhiteSpace(lines[i - 1]) || !string.IsNullOrWhiteSpace(lines[i + 1]))
        {
            return null;
        }

        Match numberListMatch = NumberListPrefix.Match(lines[i]);

        if (!numberListMatch.Success)
        {
            return null;
        }

        return new TpnSectionHeader(
            numberListMatch.Groups["name"].Value,
            lines[i],
            TpnSectionHeaderFormat.Numbered,
            i,
            1);
    }
}
