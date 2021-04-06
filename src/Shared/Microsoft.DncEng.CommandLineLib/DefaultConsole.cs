// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.CommandLineLib
{
    public class DefaultConsole : IConsole
    {
        private readonly IConsoleBackend _console;
        private readonly Lazy<GlobalCommand> _global;

        public DefaultConsole(IConsoleBackend console, ICommandOptions options)
        {
            _console = console;
            _global = new Lazy<GlobalCommand>(options.GetOptions<GlobalCommand>);
        }

        public bool ShouldWrite(VerbosityLevel level)
        {
            return level <= _global.Value.Verbosity;
        }

        public void Write(VerbosityLevel level, string message, ConsoleColor? color)
        {
            LogImpl(_console.Out, level, message, color);
        }

        public void Error(VerbosityLevel level, string message, ConsoleColor? color)
        {
            LogImpl(_console.Error, level, message, color);
        }

        public async Task<bool> ConfirmAsync(string message, string requiredWord)
        {
            var line = await PromptAsync(message);
            return string.Equals(line, requiredWord, StringComparison.CurrentCultureIgnoreCase);
        }

        public Task<string> PromptAsync(string message)
        {
            return _console.PromptAsync(message);
        }

        public bool IsInteractive => _console.IsInteractive;

        private void LogImpl(TextWriter writer, VerbosityLevel level, string message, ConsoleColor? color)
        {
            if (!ShouldWrite(level))
            {
                return;
            }

            if (color.HasValue)
            {
                _console.SetColor(color.Value);
            }

            writer.Write(message);
            if (color.HasValue)
            {
                _console.ResetColor();
            }
        }
    }
}
