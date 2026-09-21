// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Web;
using AwesomeAssertions;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.Resources;
using Maestro.WorkItems;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Tools.Cli.Common.Operations;
using Tools.Cli.Common.Options;

namespace ProductConstructionService.Cli.Tests;

public class DeploymentOperationTests
{
    [TestCase("test-app--candidate-1")]
    [TestCase("test-app--candidate-2")]
    public void GetLogsUri_EncodesQueryForCandidateRevision(string revisionName)
    {
        var options = new DeploymentOptions
        {
            SubscriptionId = "00000000-0000-0000-0000-000000000001",
            ResourceGroupName = "test-resource-group",
            ContainerAppName = "test-app",
            NewImageTag = "test-tag",
            ContainerRegistryName = "test-registry",
            WorkspaceName = "test-workspace",
            ImageName = "test-image",
            ContainerJobNames = "",
            AzCliPath = "",
            RedisConnectionString = ""
        };
        var coordinator = new ReplicaStateCoordinator(
            Mock.Of<IWorkItemProcessorReplicaProvider>(),
            Mock.Of<IWorkItemProcessorStateStore>(),
            NullLogger<ReplicaStateCoordinator>.Instance);
        var operation = new DeploymentOperation(
            options,
            Mock.Of<IProcessManager>(),
            NullLogger<DeploymentOperation>.Instance,
            Mock.Of<ResourceGroupResource>(),
            coordinator,
            Mock.Of<ContainerAppResource>());

        var logsUri = operation.GetLogsUri(revisionName);

        var expectedPrefix = "https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/" +
            $"resourceId/%2Fsubscriptions%2F{options.SubscriptionId}%2FresourceGroups%2F{options.ResourceGroupName}%2Fproviders%2FMicrosoft.OperationalInsights%2Fworkspaces%2F" +
            $"{options.WorkspaceName}/source/LogsBlade.AnalyticsShareLinkToQuery/q/";
        const string expectedSuffix = "/timespan/P1D/limit/1000";
        logsUri.Should().StartWith(expectedPrefix).And.EndWith(expectedSuffix);
        var encodedQuery = logsUri[expectedPrefix.Length..^expectedSuffix.Length];
        encodedQuery.Should().NotContain("/");
        using var compressedQuery = new MemoryStream(Convert.FromBase64String(HttpUtility.UrlDecode(encodedQuery)));
        using var decompressedQuery = new GZipStream(compressedQuery, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressedQuery);
        reader.ReadToEnd().Should().Be($"""
            ContainerAppConsoleLogs_CL
            | where RevisionName_s == '{revisionName}'
            | project TimeGenerated, Log_s
            """);
    }
}
