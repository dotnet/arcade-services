// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Microsoft.DncEng.CommandLineLib
{
    public class DefaultCommandRegistry : ICommandRegistry
    {
        private readonly ImmutableList<(Type parent, string name, Type commandType)> _commandSet;

        public DefaultCommandRegistry()
        {
            _commandSet = ScanForCommands();
        }

        private static ImmutableList<(Type parent, string name, Type commandType)> ScanForCommands()
        {
            Type commandType = typeof(Command);
            IEnumerable<Type> allCommands =
                Assembly.GetEntryAssembly().GetTypes().Where(t => commandType.IsAssignableFrom(t));
            ImmutableList<(Type parent, string name, Type commandType)>.Builder list =
                ImmutableList.CreateBuilder<(Type parent, string name, Type commandType)>();
            foreach (Type command in allCommands)
            {
                if (command.IsAbstract)
                {
                    continue;
                }

                var attr = command.GetCustomAttribute<CommandAttribute>();
                if (attr == null)
                {
                    continue;
                }

                Type parentType = attr.Parent ?? typeof(GlobalCommand);

                list.Add((parentType, attr.Name, command));
            }

            return list.ToImmutable();
        }

        public IReadOnlyDictionary<string, Type> GetValidCommandAtScope(Type scope = null)
        {
            scope ??= typeof(GlobalCommand);
            return _commandSet.Where(c => c.parent == scope).ToImmutableDictionary(c => c.name, c => c.commandType);
        }
    }
}
