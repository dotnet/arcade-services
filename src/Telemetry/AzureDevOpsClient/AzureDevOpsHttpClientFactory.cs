using Microsoft.DotNet.Services.Utility;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
    
namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public class AzureDevOpsHttpClientFactory : IHttpClientFactory
    {
        private IEnumerable<AzureDevOpsDelegatingHandler> _delegatingHandlers;

        public AzureDevOpsHttpClientFactory(IEnumerable<AzureDevOpsDelegatingHandler> delegatingHandlers = null)
        {
            _delegatingHandlers = delegatingHandlers;
        }

        public HttpClient CreateClient(string name)
        {
            return HttpClientFactory.Create(new HttpClientHandler { CheckCertificateRevocationList = true }, _delegatingHandlers.ToArray());
        }
    }
}
