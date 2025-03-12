// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace ProductConstructionService.BarViz.Code.Helpers;

internal static class StringExtensions
{
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

        return terms.ToArray();
    }
}
