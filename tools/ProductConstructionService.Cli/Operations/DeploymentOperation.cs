// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Text.Json;
using System.Web;
using Azure;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Cli.Operations;

internal class DeploymentOperation : IOperation
{
    private readonly DeploymentOptions _options;
    private readonly ResourceGroupResource _resourceGroup;
    private ContainerAppResource _containerApp;
    private readonly IProcessManager _processManager;
    private readonly ILogger<DeploymentOperation> _logger;
    private readonly IReplicaWorkItemProcessorStateCacheFactory _replicaWorkItemProcessorStateFactory;

    private const int SleepTimeSeconds = 10;
    private const int MaxStopAttempts = 100;

    public DeploymentOperation(
        DeploymentOptions options,
        IProcessManager processManager,
        ILogger<DeploymentOperation> logger,
        ResourceGroupResource resourceGroup,
        IReplicaWorkItemProcessorStateCacheFactory replicaWorkItemProcessorStateFactory,
        ContainerAppResource containerApp)
    {
        _options = options;
        _processManager = processManager;
        _logger = logger;
        _resourceGroup = resourceGroup;
        _replicaWorkItemProcessorStateFactory = replicaWorkItemProcessorStateFactory;
        _containerApp = containerApp;
    }

    private string[] DefaultAzCliParameters => [
        "--name", _options.ContainerAppName,
        "--resource-group", _options.ResourceGroupName,
        ];
    private readonly RevisionRunningState _runningAtMaxScaleState = new("RunningAtMaxScale");

    public async Task<int> RunAsync()
    {
        var trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();

        var activeRevisionTrafficWeight = trafficWeights.FirstOrDefault(weight => weight.Weight == 100) ??
            throw new ArgumentException("Container app has no active revision, please investigate manually");

        bool newRevisionDeployed;
        // When we create the ACA, the first revision won't have a name
        if (activeRevisionTrafficWeight.RevisionName == null)
        {
            newRevisionDeployed = await DeployNewRevision(inactiveRevisionLabel: "blue");
        }
        else
        {
            var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(activeRevisionTrafficWeight.RevisionName)).Value;
            var replicas = activeRevision.GetContainerAppReplicas().ToList();

            _logger.LogInformation("Currently active revision {revisionName} with label {label}",
                activeRevisionTrafficWeight.RevisionName,
                activeRevisionTrafficWeight.Label);

            // Determine the label of the inactive revision
            var inactiveRevisionLabel = activeRevisionTrafficWeight.Label == "blue" ? "green" : "blue";

            _logger.LogInformation("Next revision will be deployed with label {inactiveLabel}", inactiveRevisionLabel);
            _logger.LogInformation("Removing label {inactiveLabel} from inactive revision", inactiveRevisionLabel);

            // Cleanup all revisions except the currently active one
            await CleanupRevisionsAsync(trafficWeights.Where(weight => weight != activeRevisionTrafficWeight));

            // Finish current work items and stop processing new ones
            await StopProcessingNewJobs(activeRevisionTrafficWeight.RevisionName);

            newRevisionDeployed = await DeployNewRevision(inactiveRevisionLabel);
            if (newRevisionDeployed)
            {
                await DeactivateCurrentRevision(activeRevisionTrafficWeight.RevisionName, activeRevisionTrafficWeight.Label);
            }
        }

        return newRevisionDeployed ? 0 : -1;
    }

    private async Task<bool> DeployNewRevision(string inactiveRevisionLabel)
    {
        var newImageFullUrl = $"{_options.ContainerRegistryName}.azurecr.io/{_options.ImageName}:{_options.NewImageTag}";
        try
        {
            // Kick off the deployment of the new image
            var newRevisionName = await DeployContainerApp(newImageFullUrl);

            // While we're waiting for the new revision to become active, deploy container jobs
            await DeployContainerJobs(newImageFullUrl);

            // Wait for the new app revision to become active
            var newRevisionActive = await WaitForRevisionToBecomeActive(newRevisionName);

            // If the new revision is active, the rollout succeeded, assign a label, transfer all traffic to it,
            // and deactivate the previously running revision
            if (newRevisionActive)
            {
                await AssignLabelAndTransferTraffic(newRevisionName, inactiveRevisionLabel);
            }
            // If the new revision is not active, deactivate it and get print log link
            else
            {
                await DeactivateFailedRevisionAndGetLogs(newRevisionName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("An error occurred: {exception}", ex);
            return false;
        }
        finally
        {
            // Start the service again. If the deployment failed, we'll activate the old revision, otherwise, we'll activate the new one
            _logger.LogInformation("Starting the service again");
            await StartActiveRevision();
        }

        return true;
    }

    private async Task DeactivateCurrentRevision(string revisionName, string revisionLabel)
    {
        try
        {
            await WaitForRevisionToStop(revisionName);

            await RemoveRevisionLabel(revisionName, revisionLabel);
            await DeactivateRevision(revisionName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to deactivate revision {revisionName}: {exception}", revisionName, ex);
        }
    }

    private async Task RemoveRevisionLabel(string revisionName, string label)
    {
        var result = await InvokeAzCLI(
            ["containerapp", "revision", "label", "remove"],
            ["--label", label]);
        result.ThrowIfFailed($"Failed to remove label {label} from revision {revisionName}.");
    }

    private async Task CleanupRevisionsAsync(IEnumerable<ContainerAppRevisionTrafficWeight> revisionsTrafficWeight)
    {
        IEnumerable<ContainerAppRevisionResource> activeRevisions = _containerApp.GetContainerAppRevisions()
            .AsEnumerable()
            .Where(revision => revision.Data.IsActive ?? false)
            .Where(revision => revision.Data.TrafficWeight != 100);

        var revisionsToDeactivate = activeRevisions
            .Select(revision => (
                revision.Data.Name,
                revisionsTrafficWeight.FirstOrDefault(trafficWeight => trafficWeight.RevisionName == revision.Data.Name)?.Label));

        foreach (var revision in revisionsToDeactivate)
        {
            if (!string.IsNullOrEmpty(revision.Label))
            {
                await RemoveRevisionLabel(revision.Name, revision.Label);
            }
            await DeactivateRevision(revision.Name);
        }
    }

    private async Task<string> DeployContainerApp(string imageUrl)
    {
        _logger.LogInformation("Deploying container app");

        var revisionSuffix = _options.NewImageTag;
        if (!string.IsNullOrEmpty(_options.Attempt))
        {
            revisionSuffix += $"-{_options.Attempt}";
        }

        var result = await InvokeAzCLI(
            ["containerapp", "update"],
            ["--image", imageUrl, "--revision-suffix", revisionSuffix]);

        result.ThrowIfFailed("Failed to deploy container app.");
        var containerapp = JsonDocument.Parse(result.StandardOutput);
        if (containerapp.RootElement.TryGetProperty("properties", out var properties) &&
            properties.TryGetProperty("latestRevisionName", out var latestRevisionName))
        {
            _logger.LogInformation("Container app revision {name} deployed", latestRevisionName.GetString());
            return latestRevisionName.GetString() ?? throw new Exception("Failed to get the latest revision name from the container app deployment response.");
        }

        throw new Exception("Failed to get the latest revision name from the container app deployment response.");
    }

    private async Task DeployContainerJobs(string imageUrl)
    {
        foreach (var jobName in _options.ContainerJobNames.Split(','))
        {
            _logger.LogInformation("Deploying container job {jobName}", jobName);
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
        _logger.LogInformation("Waiting for revision {revisionName} to become active", revisionName);
        RevisionRunningState status;
        do
        {
            var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
            status = revision.Data.RunningState ?? RevisionRunningState.Unknown;
        }
        while (await SleepIfTrue(
            () => status != _runningAtMaxScaleState && status != RevisionRunningState.Failed,
            SleepTimeSeconds));

        return status == _runningAtMaxScaleState;
    }

    private async Task AssignLabelAndTransferTraffic(string revisionName, string label)
    {
        _logger.LogInformation("Assigning label {label} to the new revision", label);

        var result = await InvokeAzCLI([
            "containerapp", "revision", "label", "add",
        ],
        [
            "--label", label, "--revision", revisionName
        ]);
        result.ThrowIfFailed($"Failed to assign label {label} to revision {revisionName}. Stderr: {result.StandardError}");

        _logger.LogInformation("Transferring all traffic to the new revision");
        result = await InvokeAzCLI([
            "containerapp", "ingress", "traffic", "set",
        ],
        [
            "--label-weight", $"{label}=100"
        ]);
        result.ThrowIfFailed($"Failed to transfer all traffic to revision {revisionName}");

        _logger.LogInformation("New revision {revisionName} is now active with label {label} and all traffic is transferred to it.",
            revisionName,
            label);
    }

    private async Task DeactivateRevision(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        await revision.DeactivateRevisionAsync();
        _logger.LogInformation("Deactivated revision {revisionName}", revisionName);
    }

    private async Task DeactivateFailedRevisionAndGetLogs(string revisionName)
    {
        await DeactivateRevision(revisionName);

        _logger.LogInformation("Check logs too see failure reason: {logsUri}", GetLogsUri());
    }

    private string GetLogsUri()
    {
        var query = """
            ContainerAppConsoleLogs_CL `
            | where RevisionName_s == '$revisionName' `
            | project TimeGenerated, Log_s
            """;

        var encodedQuery = ConvertStringToCompressedBase64EncodedQuery(query);

        return "https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/" +
           $"resourceId/%2Fsubscriptions%2F{_options.SubscriptionId}%2FresourceGroups%2F{_options.ResourceGroupName}%2Fproviders%2FMicrosoft.OperationalInsights%2Fworkspaces%2F" +
           $"{_options.WorkspaceName}/source/LogsBlade.AnalyticsShareLinkToQuery/q/{encodedQuery}/timespan/P1D/limit/1000";
    }

    private async Task<ProcessExecutionResult> InvokeAzCLI(string[] command, string[] parameters)
    {
        string[] fullCommand = [.. command, .. DefaultAzCliParameters, .. parameters];
        _logger.LogInformation("Invoking az cli command `{command}`", string.Join(' ', fullCommand));
        return await _processManager.Execute(
            Path.GetFileName(_options.AzCliPath),
            fullCommand,
            workingDir: Path.GetDirectoryName(_options.AzCliPath));
    }

    private async Task WaitForRevisionToStop(string revisionName)
    {
        var replicaStateCaches = await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStateCachesAsync(revisionName);

        int count;
        for (count = 0; count < MaxStopAttempts; count++)
        {
            var states = replicaStateCaches.Select(replica => replica.GetStateAsync()).ToArray();

            await Task.WhenAll(states);

            if (states.All(state => state.Result == WorkItemProcessorState.Stopped))
            {
                break;
            }

            _logger.LogInformation("Waiting for revision {revisionName} to stop", revisionName);
            await Task.Delay(TimeSpan.FromSeconds(SleepTimeSeconds));
        }

        if (count == MaxStopAttempts)
        {
            _logger.LogError("Revision {revisionName} failed to stop after {attempts} seconds.", revisionName, MaxStopAttempts * SleepTimeSeconds);
        }
    }

    private async Task StopProcessingNewJobs(string revisionName)
    {
        _logger.LogInformation("Stopping the service from processing new jobs");

        var replicaStateCaches = await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStateCachesAsync(revisionName);
        try
        {
            foreach (var replicaStateCache in replicaStateCaches)
            {
                await replicaStateCache.SetStateAsync(WorkItemProcessorState.Stopping);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("An error occurred: {ex}. Deploying the new revision without stopping the service", ex);
        }
    }

    private async Task StartActiveRevision()
    {
        // refresh the containerApp resource
        _containerApp = await _containerApp.GetAsync();

        // Get the name of the currently active revision
        var activeRevisionName = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(trafficWeight => trafficWeight.Weight == 100)
            .RevisionName;

        _logger.LogInformation("Starting all replicas of the {revisionName} revision", activeRevisionName);
        var replicaStateCaches = await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStateCachesAsync(activeRevisionName);
        var tasks = replicaStateCaches.Select(replicaStateCache => replicaStateCache.SetStateAsync(WorkItemProcessorState.Working)).ToArray();

        await Task.WhenAll(tasks);
    }

    private static async Task<bool> SleepIfTrue(Func<bool> condition, int durationSeconds)
    {
        if (condition())
        {
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
            return true;
        }

        return false;
    }

    private static string ConvertStringToCompressedBase64EncodedQuery(string query)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(query);
        MemoryStream memoryStream = new();
        GZipStream compressedStream = new(memoryStream, CompressionMode.Compress);

        compressedStream.Write(bytes, 0, bytes.Length);
        compressedStream.Close();
        memoryStream.Seek(0, SeekOrigin.Begin);
        var data = memoryStream.ToArray();
        var base64query = Convert.ToBase64String(data);
        return HttpUtility.UrlEncode(base64query);
    }
}
