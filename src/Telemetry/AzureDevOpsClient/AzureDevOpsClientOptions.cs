// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public class AzureDevOpsClientOptions
    {
        public List<AzureDevOpsSettings> Settings { get; set; }
    }
    public class AzureDevOpsSettings
    {
        public string BaseUrl { get; set; }
        public string Organization { get; set; }
        public int MaxParallelRequests { get; set; }
        public string AccessToken { get; set; }
    }
}
