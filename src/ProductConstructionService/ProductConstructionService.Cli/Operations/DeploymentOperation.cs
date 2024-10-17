// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
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
    private readonly IReplicaWorkItemProcessorStateFactory _replicaWorkItemProcessorStateFactory;

    private const int SleepTimeSeconds = 10;
    private const int MaxStopAttempts = 100;

    public DeploymentOperation(
        DeploymentOptions options,
        IProcessManager processManager,
        ILogger<DeploymentOperation> logger,
        ResourceGroupResource resourceGroup,
        IReplicaWorkItemProcessorStateFactory replicaWorkItemProcessorStateFactory,
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
    private readonly RevisionRunningState RunningAtMaxScaleState = new RevisionRunningState("RunningAtMaxScale");

    public async Task<int> RunAsync()
    {
        var trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();

        var activeRevisionTrafficWeight = trafficWeights.FirstOrDefault(weight => weight.Weight == 100) ??
            throw new ArgumentException("Container app has no active revision, please investigate manually");
        string inactiveRevisionLabel;
        // When we create the ACA, the first revision won't have a name
        if (activeRevisionTrafficWeight.RevisionName == null)
        {
            inactiveRevisionLabel = "blue";
        }
        else
        {
            var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(activeRevisionTrafficWeight.RevisionName)).Value;
            var replicas = activeRevision.GetContainerAppReplicas().ToList();

            _logger.LogInformation("Currently active revision {revisionName} with label {label}",
                activeRevisionTrafficWeight.RevisionName,
                activeRevisionTrafficWeight.Label);

            // Determine the label of the inactive revision
            inactiveRevisionLabel = activeRevisionTrafficWeight.Label == "blue" ? "green" : "blue";

            _logger.LogInformation("Next revision will be deployed with label {inactiveLabel}", inactiveRevisionLabel);
            _logger.LogInformation("Removing label {inactiveLabel} from inactive revision", inactiveRevisionLabel);

            // Cleanup all revisions except the currently active one
            await CleanupRevisionsAsync(trafficWeights.Where(weight => weight != activeRevisionTrafficWeight));

            // Tell the active revision to finish current work items and stop processing new ones
            await StopProcessingNewJobs(activeRevisionTrafficWeight.RevisionName);
        }

        var newRevisionName = $"{_options.ContainerAppName}--{_options.NewImageTag}";
        var newImageFullUrl = $"{_options.ContainerRegistryName}.azurecr.io/{_options.ImageName}:{_options.NewImageTag}";
        try
        {
            // Kick off the deployment of the new image
            await DeployContainerApp(newImageFullUrl);

            // While we're waiting for the new revision to become active, deploy container jobs
            await DeployContainerJobs(newImageFullUrl);

            // Wait for the new app revision to become active
            var newRevisionActive = await WaitForRevisionToBecomeActive(newRevisionName);

            // If the new revision is active, the rollout succeeded, assign a label, and transfer all traffic to it
            if (newRevisionActive)
            {
                await AssignLabelAndTransferTraffic(newRevisionName, inactiveRevisionLabel);
            }
            // If the new revision is not active, deactivate it and get print log link
            else
            {
                await DeactivateFailedRevisionAndGetLogs(newRevisionName);
                return -1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("An error occurred: {exception}", ex);
            return -1;
        }
        finally
        {
            // Start the service again. If the deployment failed, we'll activate the old revision, otherwise, we'll activate the new one
            _logger.LogInformation("Starting the service again");
            await StartActiveRevision();
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
                    ],
                    [
                        "--label", revisionTrafficWeight.Label
                    ]);
                result.ThrowIfFailed($"Failed to remove label {revisionTrafficWeight.Label} from revision {revisionTrafficWeight.RevisionName}. Stderr: {result.StandardError}");
            }
        }

        // Now deactivate all revisions in the list
        foreach (var revisionTrafficWeight in revisionsTrafficWeight)
        {
            _containerApp = await _containerApp.GetAsync();
            ContainerAppRevisionResource revision = (await _containerApp.GetContainerAppRevisionAsync(revisionTrafficWeight.RevisionName)).Value;

            await revision.DeactivateRevisionAsync();
        }
    }

    private async Task DeployContainerApp(string imageUrl)
    {
        _logger.LogInformation("Deploying container app");
        _containerApp = await _containerApp.GetAsync();
        _containerApp.Data.Template.Containers[0].Image = imageUrl;
        _containerApp.Data.Template.RevisionSuffix = _options.NewImageTag;
        await _containerApp.UpdateAsync(WaitUntil.Completed, _containerApp.Data);
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
        while (await Utility.SleepIfTrue(
            () => status != RunningAtMaxScaleState && status != RevisionRunningState.Failed,
            SleepTimeSeconds));

        return status == RunningAtMaxScaleState;
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

    private async Task DeactivateFailedRevisionAndGetLogs(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        await revision.DeactivateRevisionAsync();
        _logger.LogInformation("Deactivated revision {revisionName}", revisionName);

        _logger.LogInformation("Check revision logs too see failure reason: {logsUri}", GetLogsUri(revisionName));
    }

    private string GetLogsUri(string revisionName)
    {
        var query = """
            ContainerAppConsoleLogs_CL `
            | where RevisionName_s == '$revisionName' `
            | project TimeGenerated, Log_s
            """;

        var encodedQuery = Utility.ConvertStringToCompressedBase64EncodedQuery(query);

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

    private async Task StopProcessingNewJobs(string activeRevisionName)
    {
        _logger.LogInformation("Stopping the service from processing new jobs");

        var replicas = await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStatesAsync();
        try
        {
            foreach (var replica in replicas)
            {
                await replica.FinishWorkItemAndStopAsync();
            }

            int count;
            for (count = 0; count < MaxStopAttempts; count++)
            {
                var states = replicas.Select(replica => replica.GetStateAsync()).ToArray();

                Task.WaitAll(states);

                if (states.All(state => state.Result == WorkItemProcessorState.Stopped))
                {
                    break;
                }

                _logger.LogInformation("Waiting for current revision to stop");
                await Task.Delay(TimeSpan.FromSeconds(SleepTimeSeconds));
            }

            if (count == MaxStopAttempts)
            {
                _logger.LogError($"Current revision failed to stop after {MaxStopAttempts * SleepTimeSeconds} seconds.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"An error occurred: {ex}. Deploying the new revision without stopping the service");
        }
    }

    private async Task StartActiveRevision()
    {
        // refresh the containerApp resource
        _containerApp = await _containerApp.GetAsync();

        // Get the name of the currently active revision
        var activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(trafficWeight => trafficWeight.Weight == 100);

        _logger.LogInformation("Starting all replicas of the {revisionName} revision", activeRevisionTrafficWeight.RevisionName);
        var replicaStates = await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStatesAsync();
        var tasks = replicaStates.Select(replicaState => replicaState.SetStartAsync()).ToArray();

        Task.WaitAll(tasks);
    }
}
