using Microsoft.DotNet.Darc.Helpers;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Helpers
{
    public class UxManagerHelpers
    {
        [Fact]
        public void GetParsedCommandHandlesFoldersWithSpacesWell()
        {
            UxManager.GetParsedCommand("");
        }
    }
}
