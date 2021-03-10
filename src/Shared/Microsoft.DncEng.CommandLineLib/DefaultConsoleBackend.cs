// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.CommandLineLib
{
    public class DefaultConsoleBackend : IConsoleBackend
    {
        public TextWriter Out => Console.Out;
        public TextWriter Error => Console.Error;

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

        public async Task<bool> ConfirmAsync(string message, string requiredWord = "yes")
        {
            // Purge the input buffer so any previous "YES" answers don't implicitly answer this confirmation question
            while (Console.KeyAvailable)
            {
                Console.ReadKey(true);
            }

            await Console.Out.WriteAsync(message);

            string line = await Console.In.ReadLineAsync();

            return string.Equals(line, requiredWord, StringComparison.CurrentCultureIgnoreCase);
        }

        public bool IsInteractive => !(Console.IsOutputRedirected || Console.IsInputRedirected || Console.IsErrorRedirected);
    }
}
