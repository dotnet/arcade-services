// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps;

public class AzureDevOpsRunResourcesParameters
{
    public Dictionary<string, AzureDevOpsRepositoryResourceParameter> Repositories { get; set; }
    public Dictionary<string, AzureDevOpsPipelineResourceParameter> Pipelines { get; set; }

    public AzureDevOpsRunResourcesParameters()
    {
        Repositories = new Dictionary<string, AzureDevOpsRepositoryResourceParameter>();
        Pipelines = new Dictionary<string, AzureDevOpsPipelineResourceParameter>();
    }
}
