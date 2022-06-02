using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public class AzureDevOpsHttpClientFactory : IHttpClientFactory
    {
        private IEnumerable<DelegatingHandler> _delegatingHandlers;

        public AzureDevOpsHttpClientFactory(IEnumerable<DelegatingHandler> delegatingHandlers = null)
        {
            _delegatingHandlers = delegatingHandlers;
        }

        public HttpClient CreateClient(string name)
        {
            return HttpClientFactory.Create(new HttpClientHandler { CheckCertificateRevocationList = true }, _delegatingHandlers.ToArray());
        }
    }
}
