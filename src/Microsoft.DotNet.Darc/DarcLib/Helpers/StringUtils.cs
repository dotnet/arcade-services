// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.VisualStudio.Services.Common;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public class StringUtils
{
    public static string GetHumanReadableFileSize(string path)
    {
        var file = new FileInfo(path);

        if (!file.Exists)
        {
            return "0 B";
        }

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

    public static string GetXxHash64(string input)
    {
        var hasher = new XxHash64(0);
        byte[] inputBytes = Encoding.ASCII.GetBytes(input);
        hasher.Append(inputBytes);
        byte[] hashBytes = hasher.GetCurrentHash();
        return Convert.ToHexString(hashBytes);
    }
    public static bool IsValidLongCommitSha(string input)
    {
        if (input == null || input.Length != 40)
        {
            return false;
        }

        foreach (char c in input)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
