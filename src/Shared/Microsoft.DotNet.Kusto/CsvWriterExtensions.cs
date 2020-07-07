// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Kusto
{
    public static class CsvWriterExtensions
    {
        public static async Task WriteCsvLineAsync(this TextWriter writer, params string[] values)
        {
            if (values.Length == 0)
            {
                return;
            }

            await WriteCsvLineAsync(writer, (IEnumerable<string>) values);
        }

        public static async Task WriteCsvLineAsync(this TextWriter writer, IEnumerable<string> values)
        {
            using (IEnumerator<string> enumerator = values.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    return;
                }

                await writer.WriteAsync(Escape(enumerator.Current));

                while (enumerator.MoveNext())
                {
                    await writer.WriteAsync(',');
                    await writer.WriteAsync(Escape(enumerator.Current));
                }
            }

            await writer.WriteLineAsync();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            // https://www.ietf.org/rfc/rfc4180.txt
            if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\n"))
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string[][] ParseCsvFile(string input)
        {
            MatchCollection matches = Regex.Matches(input,
                @"
(?<=^|\r?\n) # Records begin at the beginning of the document or after a newline (technically the \r is required, but we are being generous)
(
    (^|,|(?<=\n))
    (?<record>
        [^,\r\n""]* # Boring stuff that isn't a comma or end of line or a quote
        |
        (
            "" # a quoted one
                ([^""]|"""")* # Anything that is inside, and either not a quote, or an escaped one
            "" # end quote
        )
    )
)+ # there is always a field, that's the rules, no zero field records according to the spec
(?=$|\r?\n)  # all records are either followed by a newline or end of record (technically the \r is required, but we are being generous)
",
                RegexOptions.ExplicitCapture | RegexOptions.IgnorePatternWhitespace);
            
            static string UnquoteFieldValue(string fieldValue)
            {
                if (fieldValue.Length <= 0 || fieldValue[0] != '"')
                {
                    return fieldValue;
                }

                return fieldValue.Substring(1, fieldValue.Length - 1).Replace("\"\"", "\"");
            }

            static string[] ConvertMatchToArray(Match match)
            {
                return match.Groups["record"].Captures.Select(c => UnquoteFieldValue(c.Value)).ToArray();
            }

            return matches.Select(ConvertMatchToArray).ToArray();
        }
    }
}
