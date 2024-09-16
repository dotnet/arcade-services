// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Web;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using Maestro.Common.AppCredentials;
using Microsoft.DotNet.DarcLib.Helpers;

namespace ProductConstructionService.Deployment;
public class Deployer
{
    private readonly DeploymentOptions _options;
    private ContainerAppResource _containerApp;
    private readonly ResourceGroupResource _resourceGroup;
    private readonly string _pcsFqdn;
    private readonly IProcessManager _processManager;

    private const int WaitTimeDelaySeconds = 20;

    public Deployer(DeploymentOptions options, IProcessManager processManager)
    {
        _options = options;
        _processManager = processManager;
        DefaultAzureCredential credential = new();
        ArmClient client = new(credential);
        SubscriptionResource subscription = client.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_options.SubscriptionId}"));
        _resourceGroup = subscription.GetResourceGroups().Get("product-construction-service");
        _containerApp = _resourceGroup.GetContainerApp("product-construction-int").Value;
        _pcsFqdn = _containerApp.Data.Configuration.Ingress.Fqdn;
    }

    private string StatusEndpoint => $"https://{_pcsFqdn}/status";
    private string StopEndpoint => $"{StatusEndpoint}/stop";
    private string StartEndpoint => $"{StatusEndpoint}/start";
    private string[] DefaultAzCliParameters => [
        "--name", _options.ContainerAppName,
        "--resource-group", _options.ResourceGroupName,
        ];
    private readonly RevisionRunningState RunningAtMaxScaleState = new RevisionRunningState("RunningAtMaxScale");

    public async Task<int> DeployAsync()
    {
        using var pcsStatusClient = new ProductConstructionServiceStatusClient(
            _options.EntraAppId,
            _options.IsCi,
            StatusEndpoint);

        List<ContainerAppRevisionTrafficWeight> trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();

        var activeRevisionTrafficWeight = trafficWeights.FirstOrDefault(weight => weight.Weight == 100) ??
            throw new ArgumentException("Container app has no active revision, please investigate manually");

        Console.WriteLine($"Currently active revision {activeRevisionTrafficWeight.RevisionName} with label {activeRevisionTrafficWeight.Label}");

        // Determine the label of the inactive revision
        string inactiveRevisionLabel = activeRevisionTrafficWeight.Label == "blue" ? "green" : "blue";

        Console.WriteLine($"Next revision will be deployed with label {inactiveRevisionLabel}");
        Console.WriteLine($"Removing label {inactiveRevisionLabel} from inactive revision");

        // Cleanup all revisions except the currently active one
        await CleanupRevisionsAsync(trafficWeights.Where(weight => weight != activeRevisionTrafficWeight));

        // Tell the active revision to finish current work items and stop processing new ones
        await pcsStatusClient.StopProcessingNewJobs();

        var newRevisionName = $"{_options.ContainerAppName}--{_options.NewImageTag}";
        var newImageFullUrl = $"{_options.ContainerRegistryName}.azurecr.io/{_options.ImageName}:{_options.NewImageTag}";
        try
        {
            // Kick off the deployment of the new image
            await DeployContainerApp(newImageFullUrl);

            // While we're waiting for the new revision to become active, deploy container jobs
            await DeployContainerJobs(newImageFullUrl);

            // Wait for the new app revision to become active
            bool newRevisionActive = await WaitForRevisionToBecomeActive(newRevisionName);

            // If the new revision is active, the rollout succeeded, assign a label, and transfer all traffic to it
            if (newRevisionActive)
            {
                await AssignLabelAndTransferTraffic(newRevisionName, inactiveRevisionLabel);
            }
            // If the new revision is not active, deactivate it and get print log link
            else
            {
                await DeactivateRevisionAndGetLogs(newRevisionName);
                return -1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex}");
            return -1;
        }
        finally
        {
            // Start the service again. If the deployment failed, we'll activate the old revision, otherwise, we'll activate the new one
            await pcsStatusClient.StartService();
        }
    }

    private async Task CleanupRevisionsAsync(IEnumerable<ContainerAppRevisionTrafficWeight> revisionsTrafficWeight)
    {
        // Cleanup all revision labels
        foreach (var revisionTrafficWeight in revisionsTrafficWeight)
        {
            if (!string.IsNullOrEmpty(revisionTrafficWeight.Label))
            {
                var result = await InvokeAzCLI([
                        "containerapp", "revision", "label", "remove",
                        "--label", revisionTrafficWeight.Label
                    ]);
                result.ThrowIfFailed($"Failed to remove label {revisionTrafficWeight.Label} from revision {revisionTrafficWeight.RevisionName}. Stderr: {result.StandardError}");
            }
        }

        // Now deactivate all revisions in the list
        foreach (var revisionTrafficWeight in revisionsTrafficWeight)
        {
            ContainerAppRevisionResource revision = (await _containerApp.GetContainerAppRevisionAsync(revisionTrafficWeight.RevisionName)).Value;

            await revision.DeactivateRevisionAsync();
        }
    }

    private async Task DeployContainerApp(string imageUrl)
    {
        Console.WriteLine("Deploying container app");
        _containerApp.Data.Template.Containers[0].Image = imageUrl;
        _containerApp.Data.Template.RevisionSuffix = _options.NewImageTag;
        await _containerApp.UpdateAsync(WaitUntil.Completed, _containerApp.Data);
    }

    private async Task DeployContainerJobs(string imageUrl)
    {
        foreach(var jobName in _options.ContainerJobNames.Split(','))
        {
            Console.WriteLine($"Deploying container job {jobName}");
            var containerJob = (await _resourceGroup.GetContainerAppJobAsync(jobName)).Value;
            containerJob.Data.Template.Containers[0].Image = imageUrl;

            ContainerAppJobPatch jobPatch = new()
            {
                Properties = new ContainerAppJobPatchProperties()
                {
                    Template = containerJob.Data.Template
                }
            };

            await containerJob.UpdateAsync(WaitUntil.Completed, jobPatch);
        }
    }

    private async Task<bool> WaitForRevisionToBecomeActive(string revisionName)
    {
        Console.WriteLine($"Waiting for revision {revisionName} to become active");
        RevisionRunningState status;
        do
        {
            var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
            status = revision.Data.RunningState ?? RevisionRunningState.Unknown;
        }
        while (status != RunningAtMaxScaleState && status != RevisionRunningState.Failed && await Utility.Sleep(WaitTimeDelaySeconds));

        return status == RunningAtMaxScaleState;
    }

    private async Task AssignLabelAndTransferTraffic(string revisionName, string label)
    {
        Console.WriteLine($"Assigning label {label} to the new revision");

        var result = await InvokeAzCLI([
            "containerapp", "revision", "label", "add",
            "--label", label,
            "--revision", revisionName
        ]);
        result.ThrowIfFailed($"Failed to assign label {label} to revision {revisionName}. Stderr: {result.StandardError}");

        Console.WriteLine($"Transferring all traffic to the new revision");
        result = await InvokeAzCLI([
            "containerapp", "ingress", "traffic", "set",
            "--label-weight", $"{label}=100"
        ]);
        result.ThrowIfFailed($"Failed to transfer all traffic to revision {revisionName}");

        Console.WriteLine($"New revision {revisionName} is now active with label {label} and all traffic is transferred to it.");
    }

    private async Task DeactivateRevisionAndGetLogs(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        await revision.DeactivateRevisionAsync();
        Console.WriteLine($"Deactivated revision {revisionName}");

        Console.WriteLine($"Check revision logs too see failure reason: {GetLogsLink(revisionName)}");
    }

    private string GetLogsLink(string revisionName)
    {
        string query = """
            ContainerAppConsoleLogs_CL `
            | where RevisionName_s == '$revisionName' `
            | project TimeGenerated, Log_s
            """;

        var bytes = System.Text.Encoding.UTF8.GetBytes(query);
        MemoryStream memoryStream = new();
        GZipStream compressedStream = new(memoryStream, CompressionMode.Compress);

        compressedStream.Write(bytes, 0, bytes.Length);
        compressedStream.Close();
        memoryStream.Seek(0, SeekOrigin.Begin);
        var data = memoryStream.ToArray();
        var base64query = Convert.ToBase64String(data);
        var encodedQuery = HttpUtility.UrlEncode(base64query);
        return "https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/" +
           $"resourceId/%2Fsubscriptions%2F{_options.SubscriptionId}%2FresourceGroups%2F{_options.ResourceGroupName}%2Fproviders%2FMicrosoft.OperationalInsights%2Fworkspaces%2F" +
           $"{_options.WorkspaceName}/source/LogsBlade.AnalyticsShareLinkToQuery/q/{encodedQuery}/timespan/P1D/limit/1000";
    }

    private async Task<ProcessExecutionResult> InvokeAzCLI(string[] command)
    {
        return await _processManager.Execute(
            Path.GetFileName(_options.AzCliPath),
            [
                .. command,
                .. DefaultAzCliParameters
            ],
            workingDir: Path.GetDirectoryName(_options.AzCliPath));
    }
}
