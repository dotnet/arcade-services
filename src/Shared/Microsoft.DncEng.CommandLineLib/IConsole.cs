// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;

namespace Microsoft.DncEng.CommandLineLib
{
    public interface IConsole
    {
        /// <summary>
        ///     Check if the given verbosity level is configured to write to the user
        /// </summary>
        bool ShouldWrite(VerbosityLevel level);

        /// <summary>
        ///     Write the message and the given message (and optionally color) to the user console on standard out
        /// </summary>
        void Write(VerbosityLevel level, string message, ConsoleColor? color);

        /// <summary>
        ///     Write the message and the given message (and optionally color) to the user console on standard error
        /// </summary>
        void Error(VerbosityLevel level, string message, ConsoleColor? color);

        /// <summary>
        ///     Give a prompt to the user and wait for a response.
        /// </summary>
        /// <remarks>
        ///     All pending, buffered input is consumed when this method is called, so
        ///     only keys entered after the prompt is produced are respected.
        ///     This means the input cannot be redirected in to answer this prompt (since it would
        ///     be consumed immediately)
        /// </remarks>
        /// <param name="message">Message to display to user console, it should indicate what the expected response is to continue.</param>
        /// <param name="requiredWord">Expected answer to prompt that will yield a "true" value (case insensitive)</param>
        /// <returns>True if the confirmation was accepted, false otherwise</returns>
        Task<bool> ConfirmAsync(string message, string requiredWord = "yes");

        /// <summary>
        ///    Give a prompt to the user and return their answer.
        /// </summary>
        /// <remarks>
        ///     All pending, buffered input is consumed when this method is called, so
        ///     only keys entered after the prompt is produced are respected.
        ///     This means the input cannot be redirected in to answer this prompt (since it would
        ///     be consumed immediately)
        /// </remarks>
        /// <param name="message">Message to display to the user.</param>
        /// <returns>The entered text</returns>
        Task<string> PromptAsync(string message);

        /// <summary>
        ///   Gets a value indicating whether the current console session is an interactive session. 
        /// </summary>
        /// <returns><c>true</c> if the console is an interactive session; <c>false</c> otherwise.</returns>
        bool IsInteractive { get; }
    }
}
