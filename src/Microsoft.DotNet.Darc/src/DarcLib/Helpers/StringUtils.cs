// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public class StringUtils
{
    private static readonly char s_shellQuoteChar;
    private static readonly char[] s_mustQuoteCharacters = { ' ', '\'', ',', '$', '\\' };
    private static readonly char[] s_mustQuoteCharactersProcess = { ' ', '\\', '"', '\'' };

    static StringUtils()
    {
        PlatformID pid = Environment.OSVersion.Platform;
        if ((int) pid != 128 && pid != PlatformID.Unix && pid != PlatformID.MacOSX)
        {
            s_shellQuoteChar = '"'; // Windows
        }
        else
        {
            s_shellQuoteChar = '\''; // !Windows
        }
    }

    public static string FormatArguments(params string[] arguments) => FormatArguments((IList<string>) arguments);

    public static string FormatArguments(IList<string> arguments) => string.Join(" ", QuoteForProcess(arguments) ?? Array.Empty<string>());

    private static string[]? QuoteForProcess(params string[] array)
    {
        if (array == null || array.Length == 0)
        {
            return array;
        }

        var rv = new string[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            rv[i] = QuoteForProcess(array[i]);
        }

        return rv;
    }

    public static string Quote(string? f)
    {
        if (string.IsNullOrEmpty(f))
        {
            return f ?? string.Empty;
        }

        if (f.IndexOfAny(s_mustQuoteCharacters) == -1)
        {
            return f;
        }

        var s = new StringBuilder();

        s.Append(s_shellQuoteChar);
        foreach (var c in f)
        {
            if (c == '\'' || c == '"' || c == '\\')
            {
                s.Append('\\');
            }

            s.Append(c);
        }
        s.Append(s_shellQuoteChar);

        return s.ToString();
    }

    // Quote input according to how System.Diagnostics.Process needs it quoted.
    private static string QuoteForProcess(string f)
    {
        if (string.IsNullOrEmpty(f))
        {
            return f ?? string.Empty;
        }

        if (f.IndexOfAny(s_mustQuoteCharactersProcess) == -1)
        {
            return f;
        }

        var s = new StringBuilder();

        s.Append('"');
        foreach (var c in f)
        {
            if (c == '"')
            {
                s.Append('\\');
                s.Append(c).Append(c);
            }
            else if (c == '\\')
            {
                s.Append(c);
            }
            s.Append(c);
        }
        s.Append('"');

        return s.ToString();
    }

    private static string[]? QuoteForProcess(IList<string> arguments)
    {
        if (arguments == null)
        {
            return Array.Empty<string>();
        }

        return QuoteForProcess(arguments.ToArray());
    }

    public static string GetHumanReadableFileSize(string path)
    {
        var file = new FileInfo(path);
        var size = file.Length;

        // Get absolute value
        long absolute_i = (size < 0 ? -size : size);
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (absolute_i >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = (size >> 50);
        }
        else if (absolute_i >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = (size >> 40);
        }
        else if (absolute_i >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = (size >> 30);
        }
        else if (absolute_i >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = (size >> 20);
        }
        else if (absolute_i >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = (size >> 10);
        }
        else if (absolute_i >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = size;
        }
        else
        {
            return size.ToString("0 B"); // Byte
        }
        // Divide by 1024 to get fractional value
        readable /= 1024;
        // Return formatted number with suffix
        return readable.ToString("0.### ") + suffix;
    }
}
