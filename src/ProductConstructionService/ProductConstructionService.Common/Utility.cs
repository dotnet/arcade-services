// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Web;

namespace ProductConstructionService.Common;

public static class Utility
{
    public static async Task<bool> SleepIfTrue(Func<bool> condition, int durationSeconds)
    {
        if (condition())
        {
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
            return true;
        }

        return false;
    }

    public static async Task<bool> SleepIfTrue(
        Func<bool> condition,
        int durationSeconds,
        Action onFalseAction)
    {
        if (condition())
        {
            onFalseAction();
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
            return true;
        }

        return false;
    }

    public static string ConvertStringToCompressedBase64EncodedQuery(string query)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(query);
        MemoryStream memoryStream = new();
        GZipStream compressedStream = new(memoryStream, CompressionMode.Compress);

        compressedStream.Write(bytes, 0, bytes.Length);
        compressedStream.Close();
        memoryStream.Seek(0, SeekOrigin.Begin);
        var data = memoryStream.ToArray();
        var base64query = Convert.ToBase64String(data);
        return HttpUtility.UrlEncode(base64query);
    }

    /// <summary>
    /// Waits till the AutoResetEvent is signaled, or the durationSeconds expires.
    /// If durationSeconds = -1, we'll wait indefinitely
    /// </summary>
    /// <returns>True, if event was signaled, otherwise false</returns>
    public static bool WaitIfTrue(this AutoResetEvent resetEvent, Func<bool> condition, int durationSeconds)
    {
        if (condition())
        {
            // if we were signaled, exit the loop
            return !resetEvent.WaitOne(durationSeconds == -1 ? durationSeconds : durationSeconds * 1000);
        }

        return false;
    }

}
