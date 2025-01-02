﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Build
    {
        public string GetRepository() => GitHubRepository ?? AzureDevOpsRepository;

        public string GetBranch() => GitHubBranch ?? AzureDevOpsBranch;
    }
}
