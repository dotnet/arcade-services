// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using JetBrains.Annotations;

namespace Microsoft.DncEng.CommandLineLib;

/// <summary>
///     Signifies this class is a command to be executable with helix-admin.
///     <example>
///         [Command("my-root-command", "A root command that can be executed with `helix-admin my-root-command`")]
///     </example>
///     <example>
///         [Command(typeof(MyCommandSet), "my-command", "A root command that can be executed with `helix-admin command-set
///         my-command`")]
///     </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public class CommandAttribute : Attribute
{
    /// <summary>
    ///     Denote this class as a root level command, for example for `helix-admin example-command`, this would be
    ///     <code>[Command("example-command")]</code>
    /// </summary>
    /// <param name="name">User typed command name to execute this command</param>
    public CommandAttribute(string name)
    {
        Name = name;
        Description = name;
    }

    /// <summary>
    ///     Denote this class as a command that is part of some other command set, for example for `helix-admin command-set
    ///     example-command`, this would be
    ///     <code>[Command(typeof(CommandSet), "example-command")]</code>
    /// </summary>
    /// <param name="parent">
    ///     Reference to the command the is the parent command set. This type should also have a
    ///     <code>[Command]</code> attribute
    /// </param>
    /// <param name="name">User typed command name to execute this command</param>
    public CommandAttribute(Type parent, string name)
    {
        Parent = parent;
        Name = name;
        Description = name;
    }

    /// <summary>
    ///     Name of command as parsed from command line
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     If this command is a sub command of some other command set, this is a reference
    ///     to the command set, or the "parent".
    ///     <example>
    ///         If the command be run is `helix-admin command-set my-command`,
    ///         then you would have these classes
    ///         <code>
    ///   [Command("command-set")] public class CommandSet : Command { }
    ///   [Command(typeof(CommandSet), "my-command")] public class MyCommand : Command { }
    /// </code>
    ///     </example>
    /// </summary>
    public Type Parent { get; }

    public string Description { get; set; }
}
