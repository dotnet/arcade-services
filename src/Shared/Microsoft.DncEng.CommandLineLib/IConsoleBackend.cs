// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.CommandLineLib
{
    public interface IConsoleBackend
    {
        TextWriter Out { get; }
        TextWriter Error { get; }
        void SetColor(ConsoleColor color);
        void ResetColor();
        Task<string> PromptAsync(string message);
        bool IsInteractive { get; }
    }
}
