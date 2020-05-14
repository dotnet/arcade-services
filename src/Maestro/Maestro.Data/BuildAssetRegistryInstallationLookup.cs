using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Data
{
    public class BuildAssetRegistryInstallationLookup : IInstallationLookup
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public BuildAssetRegistryInstallationLookup(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public Task<long> GetInstallationId(string repositoryUrl)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
                return ctx.GetInstallationId(repositoryUrl);
            }
        }
    }
}
