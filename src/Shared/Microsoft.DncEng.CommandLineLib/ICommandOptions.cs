// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DncEng.CommandLineLib;

/// <summary>
///     Used to get the value of other command options, such as
///     <see cref="GlobalCommand.Verbosity" />
/// </summary>
public interface ICommandOptions
{
    /// <summary>
    ///     Get the parsed options for the given command type.
    ///     If this command was not part of the current execution, and Exception is thrown
    /// </summary>
    /// <typeparam name="T">Type of command to fetch</typeparam>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the given option type has not been parsed</exception>
    /// <returns>The parsed command options</returns>
    T GetOptions<T>();
}
