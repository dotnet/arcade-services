using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public class TestConsole : IConsole
    {
        public List<(VerbosityLevel level, string message, ConsoleColor? color)> Writes { get; } = new List<(VerbosityLevel, string, ConsoleColor?)>();
        public List<(VerbosityLevel level, string message, ConsoleColor? color)> Errors { get; } = new List<(VerbosityLevel, string, ConsoleColor?)>();

        public bool ShouldWrite(VerbosityLevel level)
        {
            return true;
        }

        public void Write(VerbosityLevel level, string message, ConsoleColor? color)
        {
            Writes.Add((level, message, color));
        }

        public void Error(VerbosityLevel level, string message, ConsoleColor? color)
        {
            Errors.Add((level, message, color));
        }

        public Task<bool> ConfirmAsync(string message, string requiredWord = "yes")
        {
            throw new NotImplementedException();
        }

        public Task<string> PromptAsync(string message)
        {
            throw new NotImplementedException();
        }

        public bool IsInteractive { get; set; } = false;
    }
}
