// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.CommandLineLib;

public class DefaultConsoleBackend : IConsoleBackend
{
    public TextWriter Out => Console.Out;
    public TextWriter Error => Console.Error;
    public TextReader In => Console.In;

    public void SetColor(ConsoleColor color)
    {
        Console.ForegroundColor = color;
    }

    public void ResetColor()
    {
        Console.ResetColor();
    }

    public Task<string> ReadLineAsync()
    {
        return Console.In.ReadLineAsync();
    }

    public async Task<string> PromptAsync(string message)
    {
        // Purge the input buffer so piped in or previously typed text doesn't count
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }

        await Console.Out.WriteAsync(message);

        string line = await Console.In.ReadLineAsync();
        return line;
    }

    public bool IsInteractive => !(Console.IsOutputRedirected || Console.IsInputRedirected || Console.IsErrorRedirected);
}
