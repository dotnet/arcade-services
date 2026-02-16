// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace BuildInsights.AzureStorage.Cache;

public static class StreamHelpers
{
    /// <summary>
    /// Use pipelines to stream data from a method that writes to a stream to a method that reads form a stream
    /// </summary>
    public static async Task StreamDataAsync(Func<Stream, Task> useWritableStream, Func<Stream, Task> useReadableSteam)
    {
        var pipe = new Pipe();

        async Task Write()
        {
            await using Stream writableStream = pipe.Writer.AsStream();
            await useWritableStream(writableStream);
        }

        async Task Read()
        {
            await using Stream readableStream = pipe.Reader.AsStream();
            await useReadableSteam(readableStream);
        }

        await Task.WhenAll(Write(), Read());
    }
}
