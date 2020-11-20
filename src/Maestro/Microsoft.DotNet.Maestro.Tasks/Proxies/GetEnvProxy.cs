using System;

namespace Microsoft.DotNet.Maestro.Tasks.Proxies
{
    internal abstract class IGetEnvProxy
    {
        internal abstract string GetEnv(string key);
    }

    internal class GetEnvProxy : IGetEnvProxy
    {
        internal override string GetEnv(string key)
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
