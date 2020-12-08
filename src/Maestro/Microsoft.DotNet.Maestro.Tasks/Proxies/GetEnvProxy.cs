// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Maestro.Tasks.Proxies
{
    public interface IGetEnvProxy
    {
        public string GetEnv(string key);
    }

    internal class GetEnvProxy : IGetEnvProxy
    {
        public string GetEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"Required Environment variable {key} not found.");
            }

            return value;
        }
    }
}
