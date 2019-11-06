// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
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
    }
}
