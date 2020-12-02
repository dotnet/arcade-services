using Microsoft.DotNet.Maestro.Tasks.Proxies;

namespace Microsoft.DotNet.Maestro.Tasks.Tests.Mocks
{
    internal class GetEnvMock : IGetEnvProxy
    {
        /// <summary>
        /// Return the key as the value - testing override
        /// </summary>
        /// <param name="key">String to be returned</param>
        /// <returns></returns>
        internal override string GetEnv(string key)
        {
            if (key == "BUILD_REPOSITORY_NAME")
            {
                return "thisIsARepo";
            }

            return key;
        }
    }
}
