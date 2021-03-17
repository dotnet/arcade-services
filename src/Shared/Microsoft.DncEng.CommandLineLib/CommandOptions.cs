// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DncEng.CommandLineLib
{
    public class CommandOptions : ICommandOptions
    {
        private readonly Dictionary<Type, object> _options = new Dictionary<Type, object>();

        public T GetOptions<T>()
        {
            return (T) _options[typeof(T)];
        }

        public void RegisterOptions(Command options)
        {
            _options.Add(options.GetType(), options);
        }
    }
}
