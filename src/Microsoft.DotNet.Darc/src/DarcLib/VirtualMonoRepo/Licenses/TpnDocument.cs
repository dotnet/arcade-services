// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;

public class TpnDocument
{
    public string Preamble { get; set; }

    public IEnumerable<TpnSection> Sections { get; set; }

    public TpnDocument(string preamble, IEnumerable<TpnSection> sections)
    {
        Preamble = preamble;
        Sections = sections;
    }

    public override string ToString() =>
        Preamble + Environment.NewLine +
        string.Join(Environment.NewLine + Environment.NewLine, Sections) +
        Environment.NewLine;

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

        if (sections.Count == 0)
        {
            throw new ArgumentException($"No sections found.");
        }

        return new TpnDocument(
            string.Join('\n', lines.Take(sections.First().Header.StartLine)),
            sections);
    }
}
