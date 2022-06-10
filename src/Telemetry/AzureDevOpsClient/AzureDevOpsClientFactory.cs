// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public sealed class AzureDevOpsClientFactory : IAzureDevOpsClientFactory
    {
        public AzureDevOpsClientFactory() { }

        public IAzureDevOpsClient CreateAzureDevOpsClient(string baseUrl, string organization, int maxParallelRequests, string accessToken)
        {
            return new AzureDevOpsClient(baseUrl, organization, maxParallelRequests, accessToken, null);
        }
    }
}
