// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.KnownIssuesProcessor;

public class KnownIssuesProcessorOptions
{
    public List<AzureDevOpsProjects> AzureDevOpsProjects { get; set; }
    public string BuildAnalysisQueueEndpoint { get; set; }
    public string BuildAnalysisQueueName { get; set; }
    public string KnownIssuesRepo { get; set; }
    public bool RepositoryIssuesOnly { get; set; }
}

public class AzureDevOpsProjects
{
    public string OrgId { get; set; }
    public string ProjectId { get; set; }
    public bool IsInternal { get; set; }
}
