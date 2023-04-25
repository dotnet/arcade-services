// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;

namespace Microsoft.DncEng.CommandLineLib;

/// <summary>
///     A command that can be executed by "helix-admin".
///     Every command must include a <see cref="CommandAttribute" /> that indicates the name and description of the command
/// </summary>
public abstract class Command
{
    /// <summary>
    ///     Create the option set for this part of the command.  Any parent commands will already
    ///     have been parsed by this point, so don't need to be included (and will not be visible here).
    ///     For example, the global command has the "-v" parameter for output verbosity. This means
    ///     no other task will receive a "-v" parameter, because it will already have been
    ///     consumed by the global command.
    ///     It is best that commands include descriptions so that help text is appropriately detailed
    ///     Options, when parsed, should set properties/fields on "this", so that after parsing,
    ///     the current instance will have the appropriate values.
    ///     <example>
    ///         return new OptionSet {
    ///         {"my-parameter|m=", "Sets the value for my-parameter", m => this.Parameter = m},
    ///         }
    ///     </example>
    /// </summary>
    public virtual OptionSet GetOptions()
    {
        return new OptionSet();
    }

    /// <summary>
    ///     Detailed help text that will be displayed if the user uses "--help"
    ///     for any given command.
    ///     Defaults to the <see cref="CommandAttribute.Description" /> value
    /// </summary>
    public virtual string GetDetailedHelpText()
    {
        return GetType().GetCustomAttribute<CommandAttribute>()?.Description;
    }

    /// <summary>
    ///     If this command should accept positional arguments after being parsed, this function should consume them.
    ///     Any options from <see cref="GetOptions" /> will have been processed and removed from this list
    ///     before this method is called
    ///     Any consumed arguments should be removed from the list and not returned.
    ///     The <see cref="ConsumeIfNull" /> can be useful for dealing with parameters that can be positional or
    ///     named.
    ///     For example, if the command is "my-command queue.name today",
    ///     then this method should do something like
    ///     <example>
    ///         this.QueueName = ConsumeIfNull(this.QueueName, args);
    ///         this.TimeSpan = ConsumeIfNull(this.TimeSpan, args);
    ///         return args;
    ///     </example>
    /// </summary>
    /// <param name="args"></param>
    /// <returns>values from <see cref="args" /> that were not consumed as positional arguments</returns>
    public virtual List<string> HandlePositionalArguments(List<string> args)
    {
        return args;
    }

    /// <summary>
    ///     If this command has any required parameters, this method should validate that they are set
    ///     on "this"
    ///     <example>
    ///         public override bool AreRequiredOptionsSet()
    ///         {
    ///         return !string.IsNullOrEmpty(this.QueueName);
    ///         }
    ///     </example>
    /// </summary>
    /// <returns>True if all required parameters are set, false if some parameters are not set</returns>
    public virtual bool AreRequiredOptionsSet()
    {
        return true;
    }

    /// <summary>
    ///     Helper function for dealing with optionally named parameters.
    ///     If the value is null, it removes the first value from the list, and returns it.
    ///     If the value is not null, does nothing and returns the original value
    /// </summary>
    /// <param name="value">Value to be checked for null</param>
    /// <param name="args">Arg list to consume value from</param>
    /// <returns>The original value, or the first element from <see cref="args" /></returns>
    public static string ConsumeIfNull(string value, List<string> args)
    {
        if (value != null)
        {
            return value;
        }

        if (args.Count == 0)
        {
            return null;
        }

        string v = args[0];
        args.RemoveAt(0);
        return v;
    }

    /// <summary>
    ///     Execute the command after all parameters have been parsed and validated.
    ///     If an expected problem is encountered that should terminate and return a
    ///     fixed exit code, throw a <see cref="FailWithExitCodeException" />.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public virtual Task RunAsync(CancellationToken cancellationToken)
    {
        // This returns null (note it's a null Task, not a Task that returns null, hence is not awaitable)
        // This is because some commands don't have implementations. In particular, commands that are only
        // containers for other commands (e.g. in `helix-admin queue purge`, the "queue" command), and have
        // no actual implementation.

        // When this returns null, the main program sees that treats it as an invalid command line,
        // and will dump usage text
        return null;
    }
}
