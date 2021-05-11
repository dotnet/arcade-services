using System;

namespace Microsoft.DncEng.CommandLineLib
{
    public static class ConsoleExtensions
    {
        /// <summary>
        ///     Write to standard out, even when --quiet mode is requested
        /// </summary>
        public static void WriteImportant(this IConsole console, string message)
        {
            console.Write(VerbosityLevel.Quiet, message, null);
        }

        /// <summary>
        ///     Write to standard out in the given color, even when --quiet mode is requested
        /// </summary>
        public static void WriteImportant(this IConsole console, string message, ConsoleColor color)
        {
            console.Write(VerbosityLevel.Quiet, message, color);
        }

        /// <summary>
        ///     Write to standard out, unless when --quiet mode is requested
        /// </summary>
        public static void Write(this IConsole console, string message)
        {
            console.Write(VerbosityLevel.Normal, message, null);
        }

        /// <summary>
        ///     Write to standard out in the given color, unless when --quiet mode is requested
        /// </summary>
        public static void Write(this IConsole console, string message, ConsoleColor color)
        {
            console.Write(VerbosityLevel.Normal, message, color);
        }

        /// <summary>
        ///     Write a line to standard out, unless when --quiet mode is requested
        /// </summary>
        public static void WriteLine(this IConsole console, string message)
        {
            console.Write(VerbosityLevel.Normal, message + "\n", null);
        }

        /// <summary>
        ///     Write a line to standard out in the given color, unless when --quiet mode is requested
        /// </summary>
        public static void WriteLine(this IConsole console, string message, ConsoleColor color)
        {
            console.Write(VerbosityLevel.Normal, message + "\n", color);
        }

        /// <summary>
        ///     Write to standard out only when --verbose mode is requested
        /// </summary>
        public static void WriteVerbose(this IConsole console, string message)
        {
            console.Write(VerbosityLevel.Verbose, message, null);
        }

        /// <summary>
        ///     Write to standard out in the given color only when --verbose mode is requested
        /// </summary>
        public static void WriteVerbose(this IConsole console, string message, ConsoleColor color)
        {
            console.Write(VerbosityLevel.Verbose, message, color);
        }

        /// <summary>
        ///     Write to standard error, in red, regardless of verbosity level
        /// </summary>
        public static void WriteError(this IConsole console, string message)
        {
            console.Error(VerbosityLevel.Quiet, message, ConsoleColor.Red);
        }

        /// <summary>
        ///   Writes an error, using the VSTS logging commands if running inside VSTS. Forwards to <see cref="WriteError"/> if outside of VSTS.
        /// </summary>
        public static void LogError(this IConsole console, string message, string file, int line, int column)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ID")))
            {
                console.Write(VerbosityLevel.Quiet, $"##vso[task.logissue type=error;sourcepath={file};linenumber={line};columnnumber={column};]{message}\n", ConsoleColor.Black);
            }
            else
            {
                console.WriteError($"{file}({line},{column}): error : {message}");
            }
        }

        /// <summary>
        ///     Write to standard error, but with non color setting, regardless of verbosity level
        /// </summary>
        public static void WriteErrorNoColor(this IConsole console, string message)
        {
            console.Error(VerbosityLevel.Quiet, message, null);
        }
    }
}
