using System;
using Microsoft.DotNet.Maestro.Tasks.Proxies;

namespace Microsoft.DotNet.Maestro.Tasks.Tests.Mocks
{
    internal class VersionIdentifierMock : IVersionIdentifierProxy
    {
        internal override string GetVersion(string assetName)
        {
            if (assetName == "noVersionForThisBlob")
            {
                return "";
            }

            return "12345";
        }

        internal override string RemoveVersions(string assetName)
        {
            throw new NotImplementedException();
        }
    }
}
