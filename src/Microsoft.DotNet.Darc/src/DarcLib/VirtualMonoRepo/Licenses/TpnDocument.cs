// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;

public class TpnDocument
{
    public const string LineSeparator = "\n";

    public string Preamble { get; set; }

    public ICollection<TpnSection> Sections { get; set; }

    public TpnDocument(string preamble, ICollection<TpnSection> sections)
    {
        Preamble = preamble;
        Sections = sections;
    }

    public override string ToString() =>
        Preamble + LineSeparator +
        string.Join(LineSeparator + LineSeparator, Sections) +
        LineSeparator;

    public static TpnDocument Parse(string[] lines)
    {
        TpnSectionHeader[] headers = TpnSectionHeader.ParseAll(lines).ToArray();
        var sections = new List<TpnSection>();

        for (int i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            var headerEndLine = header.StartLine + header.LineLength + 1;
            var linesUntilNext = lines.Length - headerEndLine;

            if (i + 1 < headers.Length)
            {
                linesUntilNext = headers[i + 1].StartLine - headerEndLine;
            }

            var content = lines
                .Skip(headerEndLine)
                .Take(linesUntilNext)
                // Skip lines in the content that could be confused for separators
                .Where(line => !TpnSectionHeader.IsSeparatorLine(line))
                // Trim empty line at the end of the section
                .Reverse()
                .SkipWhile(string.IsNullOrWhiteSpace)
                .Reverse();
        
            sections.Add(new TpnSection(header, string.Join('\n', content)));
        }

        return new TpnDocument(
            string.Join('\n', lines.Take(sections.FirstOrDefault()?.Header.StartLine ?? lines.Length)),
            sections.Where(s => !string.IsNullOrWhiteSpace(s.Content)).ToList());
    }
}
