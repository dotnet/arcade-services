using Microsoft.DotNet.VersionTools.BuildManifest;

namespace Microsoft.DotNet.Maestro.Tasks.Proxies
{
    internal abstract class IVersionIdentifierProxy
    {
        internal abstract string GetVersion(string assetName);
        internal abstract string RemoveVersions(string assetName);
    }

    internal class VersionIdentifierProxy : IVersionIdentifierProxy
    {
        internal override string GetVersion(string assetName)
        {
            return VersionIdentifier.GetVersion(assetName);
        }

        internal override string RemoveVersions(string assetName)
        {
            return VersionIdentifier.RemoveVersions(assetName);
        }
    }
}
