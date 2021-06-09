using System;
using System.Text;

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
        ///   Writes an issue, using the VSTS logging commands if running inside VSTS. Forwards to stderr if outside of VSTS.
        /// </summary>
        public static void LogIssue(this IConsole console, IssueKind kind, string message, string file = null, int? line = null, int? column = null)
        {
            var (type, color) = (kind) switch
            {
                IssueKind.Warning => ("warning", ConsoleColor.Yellow),
                IssueKind.Error => ("error", ConsoleColor.Red),
                _ => throw new ArgumentException("Invalid IssueKind", nameof(kind)),
            };
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGENT_ID")))
            {
                console.Error(VerbosityLevel.Quiet, $"##vso[task.logissue type={type};sourcepath={file};linenumber={line};columnnumber={column};]{message}\n", ConsoleColor.Black);
            }
            else
            {
                var m = new StringBuilder();
                if (!string.IsNullOrEmpty(file))
                {
                    m.Append(file);
                    if (line != null)
                    {
                        if (column != null)
                        {
                            m.Append($"({line},{column})");
                        }
                        else
                        {
                            m.Append($"({line})");
                        }
                    }

                    m.Append(": ");
                }

                m.Append($"{type} : {message}\n");
                console.Error(VerbosityLevel.Quiet, m.ToString(), color);
            }
        }

        /// <summary>
        ///   Writes an error, using the VSTS logging commands if running inside VSTS. Forwards to stderr if outside of VSTS.
        /// </summary>
        public static void LogError(this IConsole console, string message, string file = null, int? line = null, int? column = null)
        {
            console.LogIssue(IssueKind.Error, message, file, line, column);
        }

        /// <summary>
        ///   Writes a warning, using the VSTS logging commands if running inside VSTS. Forwards to stderr if outside of VSTS.
        /// </summary>
        public static void LogWarning(this IConsole console, string message, string file = null, int? line = null, int? column = null)
        {
            console.LogIssue(IssueKind.Warning, message, file, line, column);
        }

        /// <summary>
        ///     Write to standard error, but with non color setting, regardless of verbosity level
        /// </summary>
        public static void WriteErrorNoColor(this IConsole console, string message)
        {
            console.Error(VerbosityLevel.Quiet, message, null);
        }
    }

    public enum IssueKind
    {
        Warning,
        Error
    }
}
