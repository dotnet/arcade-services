using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Rest;
using Microsoft.Rest.TransientFaultHandling;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal static class Helpers
    {
        private const string MsftAdTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        private static readonly AzureServiceTokenProvider TokenProvider = new AzureServiceTokenProvider();

        private class AzureCredentialsTokenProvider : ITokenProvider
        {
            private readonly AzureServiceTokenProvider _inner;

            public AzureCredentialsTokenProvider(AzureServiceTokenProvider inner)
            {
                _inner = inner;
            }

            public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken)
            {
                string token = await _inner.GetAccessTokenAsync("https://management.azure.com", MsftAdTenantId);
                return new AuthenticationHeaderValue("Bearer", token);
            }
        }

        public static (IAzure, IResourceManager) Authenticate(string subscriptionId)
        {
            string version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";

            var tokenCredentials = new TokenCredentials(new AzureCredentialsTokenProvider(TokenProvider));
            var credentials = new AzureCredentials(tokenCredentials, null, MsftAdTenantId, AzureEnvironment.AzureGlobalCloud);

            HttpLoggingDelegatingHandler.Level logLevel = HttpLoggingDelegatingHandler.Level.Headers;
            var retryPolicy = new RetryPolicy(new DefaultTransientErrorDetectionStrategy(), 5);
            var programName = "DncEng Service Fabric Cluster Creator";

            return (Azure.Management.Fluent.Azure.Configure()
                    .WithLogLevel(logLevel)
                    .WithRetryPolicy(retryPolicy)
                    .WithUserAgent(programName, version)
                    .Authenticate(credentials)
                    .WithSubscription(subscriptionId),
                ResourceManager.Configure()
                    .WithLogLevel(logLevel)
                    .WithRetryPolicy(retryPolicy)
                    .WithUserAgent(programName, version)
                    .Authenticate(credentials)
                    .WithSubscription(subscriptionId));
        }
    }
}
