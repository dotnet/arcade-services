using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal class HostEnvironment : IHostEnvironment
    {
        public HostEnvironment(string environmentName, string contentRootPath)
        {
            EnvironmentName = environmentName;
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
