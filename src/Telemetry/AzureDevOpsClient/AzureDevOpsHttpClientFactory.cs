// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Services.Utility;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
    
namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public class AzureDevOpsHttpClientFactory : IAzureDevOpsHttpClientFactory
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
