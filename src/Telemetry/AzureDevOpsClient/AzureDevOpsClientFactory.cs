// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public sealed class AzureDevOpsClientFactory : IAzureDevOpsClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AzureDevOpsClientFactory(IHttpClientFactory httpClientFactory) 
        {
            _httpClientFactory = httpClientFactory;
        }

        public IAzureDevOpsClient CreateAzureDevOpsClient(
            string baseUrl,
            string organization,
            int maxParallelRequests,
            string accessToken)
        {
            return new AzureDevOpsClient(new OptionsWrapper<AzureDevOpsClientOptions>(new AzureDevOpsClientOptions()
            {
                AccessToken = accessToken,
                BaseUrl = baseUrl,
                Organization = organization,
                MaxParallelRequests = maxParallelRequests
            }), _httpClientFactory);
        }
    }
}
