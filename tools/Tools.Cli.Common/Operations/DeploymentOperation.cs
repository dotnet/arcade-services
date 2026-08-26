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
using Tools.Cli.Core;
using Tools.Cli.Common.Options;
using Maestro.WorkItems;

namespace Tools.Cli.Common.Operations;

public class DeploymentOperation : IOperation
{
    private readonly DeploymentOptions _options;
    private readonly ResourceGroupResource _resourceGroup;
    private ContainerAppResource _containerApp;
    private readonly IProcessManager _processManager;
    private readonly ILogger<DeploymentOperation> _logger;
    private readonly ReplicaStateCoordinator _replicaStateCoordinator;

    private const int SleepTimeSeconds = ReplicaStateCoordinator.PollIntervalSeconds;
    private const int MaxStopAttempts = ReplicaStateCoordinator.MaxPollAttempts;

    /// <summary>
    /// How long a candidate revision may stay non failed but unhealthy before we warn about a slow natural start.
    /// </summary>
    private const int RevisionNaturalStartWarningThresholdSeconds = 180;

    /// <summary>
    /// How long a stale failed or inactive revision status is tolerated after an explicit activation.
    /// </summary>
    private const int RevisionActivationPropagationGracePeriodSeconds = 60;

    private const string BlueLabel = "blue";
    private const string GreenLabel = "green";

    public DeploymentOperation(
        DeploymentOptions options,
        IProcessManager processManager,
        ILogger<DeploymentOperation> logger,
        ResourceGroupResource resourceGroup,
        ReplicaStateCoordinator replicaStateCoordinator,
        ContainerAppResource containerApp)
    {
        _options = options;
        _processManager = processManager;
        _logger = logger;
        _resourceGroup = resourceGroup;
        _replicaStateCoordinator = replicaStateCoordinator;
        _containerApp = containerApp;
    }

    private string[] DefaultAzCliParameters => [
        "--name", _options.ContainerAppName,
        "--resource-group", _options.ResourceGroupName,
        ];

    private readonly RevisionRunningState _runningAtMaxScaleState = new("RunningAtMaxScale");

    public async Task<int> RunAsync()
    {
        string? oldRevisionName = null;
        string? candidateRevisionName = null;
        var oldRevisionStopRequested = false;

        try
        {
            var trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();
            var activeRevisionTrafficWeight = trafficWeights.FirstOrDefault(weight => weight.Weight == 100) ??
                throw new ArgumentException("Container app has no active revision, please investigate manually");

            oldRevisionName = activeRevisionTrafficWeight.RevisionName;
            var oldRevisionLabel = activeRevisionTrafficWeight.Label;
            var inactiveRevisionLabel = oldRevisionLabel == BlueLabel ? GreenLabel : BlueLabel;

            _logger.LogInformation("Currently active revision {revisionName} with label {label}", oldRevisionName, oldRevisionLabel);
            _logger.LogInformation("Next revision will be deployed with label {inactiveLabel}", inactiveRevisionLabel);

            var revisionsBeforeUpdate = _containerApp.GetContainerAppRevisions()
                .AsEnumerable()
                .ToDictionary(revision => revision.Data.Name, revision => revision.Data.IsActive ?? false, StringComparer.OrdinalIgnoreCase);

            await CleanupRevisionsAsync(trafficWeights.Where(weight => weight != activeRevisionTrafficWeight));

            var newImageFullUrl = $"{_options.ContainerRegistryName}.azurecr.io/{_options.ImageName}:{_options.NewImageTag}";
            candidateRevisionName = await DeployContainerApp(newImageFullUrl);
            var wasReusedInactiveCandidate = revisionsBeforeUpdate.TryGetValue(candidateRevisionName, out var wasActive) && !wasActive;

            if (!await WaitForRevisionToBecomeHealthy(candidateRevisionName, wasReusedInactiveCandidate))
            {
                _logger.LogError("Check logs to see the failure reason: {logsUri}", GetLogsUri());
                await StopDeactivateAndCleanupRevision(candidateRevisionName);
                return -1;
            }

            if (!string.IsNullOrEmpty(oldRevisionName))
            {
                oldRevisionStopRequested = true;
                if (!await _replicaStateCoordinator.SetDesiredStateAndWaitAsync(
                    oldRevisionName,
                    WorkItemProcessorState.Stopped,
                    requireAtLeastOneReplica: false))
                {
                    _logger.LogWarning(
                        "Revision {revisionName} did not confirm it stopped before the configured timeout; continuing with a best effort switch to revision {candidateRevisionName}",
                        oldRevisionName,
                        candidateRevisionName);
                }
            }

            if (!await _replicaStateCoordinator.SetDesiredStateAndWaitAsync(
                candidateRevisionName,
                WorkItemProcessorState.Working,
                requireAtLeastOneReplica: true))
            {
                _logger.LogError("Revision {revisionName} did not start processing before the configured timeout", candidateRevisionName);
                await Compensate(oldRevisionName, candidateRevisionName);
                return -1;
            }

            if (!await TransferTraffic(candidateRevisionName, oldRevisionName, inactiveRevisionLabel))
            {
                return -1;
            }

            if (!string.IsNullOrEmpty(oldRevisionName))
            {
                if (!string.IsNullOrEmpty(oldRevisionLabel))
                {
                    await RemoveRevisionLabel(oldRevisionName, oldRevisionLabel);
                }

                await StopDeactivateAndCleanupRevision(oldRevisionName);
            }

            await DeployContainerJobs(newImageFullUrl);

            _logger.LogInformation("Deployment completed successfully. Active revision is {revisionName}", candidateRevisionName);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during deployment");

            if (oldRevisionStopRequested)
            {
                await Compensate(oldRevisionName, candidateRevisionName);
            }

            return -1;
        }
    }

    private async Task<bool> TransferTraffic(string candidateRevisionName, string? oldRevisionName, string label)
    {
        try
        {
            await AssignLabelAndTransferTraffic(candidateRevisionName, label);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer traffic to revision {revisionName}", candidateRevisionName);

            _containerApp = await _containerApp.GetAsync();
            var trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();

            if (HasAllTraffic(trafficWeights, candidateRevisionName))
            {
                _logger.LogInformation(
                    "Revision {revisionName} already holds all traffic, continuing the deployment",
                    candidateRevisionName);
                return true;
            }

            if (!string.IsNullOrEmpty(oldRevisionName) && HasAllTraffic(trafficWeights, oldRevisionName))
            {
                await Compensate(oldRevisionName, candidateRevisionName);
            }
            else
            {
                _logger.LogError("Traffic ownership is ambiguous after the failed transfer, leaving both revisions untouched");
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the old revision to work after the candidate failed. The candidate is stopped, deactivated and
    /// cleaned up first; if that cannot be confirmed, nothing is resumed and the candidate keeps its keys.
    /// </summary>
    private async Task Compensate(string? oldRevisionName, string? candidateRevisionName)
    {
        if (!string.IsNullOrEmpty(candidateRevisionName) && !await StopDeactivateAndCleanupRevision(candidateRevisionName))
        {
            _logger.LogError(
                "Revision {revisionName} could not be confirmed stopped, it stays active and revision {oldRevisionName} is not resumed",
                candidateRevisionName,
                oldRevisionName);
            return;
        }

        if (string.IsNullOrEmpty(oldRevisionName))
        {
            return;
        }

        _logger.LogInformation("Resuming queue processing on revision {revisionName}", oldRevisionName);
        if (!await _replicaStateCoordinator.SetDesiredStateAndWaitAsync(
            oldRevisionName,
            WorkItemProcessorState.Working,
            requireAtLeastOneReplica: true))
        {
            _logger.LogError("Revision {revisionName} did not resume queue processing", oldRevisionName);
        }
    }

    /// <summary>
    /// Observes the natural provisioning of the candidate revision. At most one explicit activation is issued,
    /// either because a reused candidate was inactive before the update, or as a one time failure recovery.
    /// </summary>
    private async Task<bool> WaitForRevisionToBecomeHealthy(string revisionName, bool wasReusedInactiveCandidate)
    {
        _logger.LogInformation("Waiting for revision {revisionName} to become healthy", revisionName);

        var naturalStartWarningPolls = ToPollCount(RevisionNaturalStartWarningThresholdSeconds);
        var activationGracePolls = ToPollCount(RevisionActivationPropagationGracePeriodSeconds);

        var explicitActivationRequested = false;
        var activationWasFailureRecovery = false;
        var recoveryProgressObserved = false;
        var slowStartWarningLogged = false;
        var deactivationWarningLogged = false;
        var wasObservedActive = false;
        var activationPoll = 0;
        var nonFailedStreakStart = 0;

        for (var attempt = 0; attempt < MaxStopAttempts; attempt++)
        {
            var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value.Data;
            var isActive = revision.IsActive ?? false;
            var hasFailed = revision.RunningState == RevisionRunningState.Failed
                || revision.ProvisioningState == ContainerAppRevisionProvisioningState.Failed
                || !string.IsNullOrEmpty(revision.ProvisioningError);

            if (hasFailed)
            {
                nonFailedStreakStart = attempt + 1;

                if (!explicitActivationRequested)
                {
                    _logger.LogWarning(
                        "Revision {revisionName} reported a terminal failure, requesting a single recovery activation",
                        revisionName);
                    await ActivateRevision(revisionName);
                    explicitActivationRequested = true;
                    activationWasFailureRecovery = true;
                    activationPoll = attempt;
                }
                else if (activationWasFailureRecovery && !recoveryProgressObserved && attempt - activationPoll < activationGracePolls)
                {
                    _logger.LogInformation(
                        "Revision {revisionName} still reports a failure after the recovery activation, waiting for the status to propagate",
                        revisionName);
                }
                else
                {
                    _logger.LogError("Revision {revisionName} failed to start", revisionName);
                    return false;
                }
            }
            else
            {
                if (isActive && revision.RunningState == _runningAtMaxScaleState)
                {
                    _logger.LogInformation("Revision {revisionName} is active and running at max scale", revisionName);
                    return true;
                }

                if (activationWasFailureRecovery && !recoveryProgressObserved)
                {
                    _logger.LogInformation("Revision {revisionName} recovered from the reported failure", revisionName);
                    recoveryProgressObserved = true;
                }

                if (isActive)
                {
                    wasObservedActive = true;
                }
                else if (wasObservedActive && !deactivationWarningLogged)
                {
                    _logger.LogWarning(
                        "Revision {revisionName} became inactive without reporting a failure, continuing to observe it",
                        revisionName);
                    deactivationWarningLogged = true;
                }

                if (wasReusedInactiveCandidate && !isActive && !explicitActivationRequested)
                {
                    _logger.LogInformation(
                        "Revision {revisionName} was reused while inactive and cannot start naturally, activating it",
                        revisionName);
                    await ActivateRevision(revisionName);
                    explicitActivationRequested = true;
                    activationPoll = attempt;
                }
                else if (explicitActivationRequested && !activationWasFailureRecovery && !isActive && attempt - activationPoll >= activationGracePolls)
                {
                    _logger.LogError(
                        "Revision {revisionName} stayed inactive for {graceSeconds} seconds after it was activated",
                        revisionName,
                        RevisionActivationPropagationGracePeriodSeconds);
                    return false;
                }
                else if (!slowStartWarningLogged && attempt - nonFailedStreakStart >= naturalStartWarningPolls)
                {
                    _logger.LogWarning(
                        "Revision {revisionName} has not become healthy within {warningSeconds} seconds but reports no failure, continuing to observe it",
                        revisionName,
                        RevisionNaturalStartWarningThresholdSeconds);
                    slowStartWarningLogged = true;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(SleepTimeSeconds));
        }

        _logger.LogError(
            "Revision {revisionName} did not become healthy within {timeoutSeconds} seconds",
            revisionName,
            MaxStopAttempts * SleepTimeSeconds);
        return false;
    }

    /// <summary>
    /// Confirms that every replica of the revision stopped processing before deactivating it and removing its keys.
    /// A revision whose stop cannot be confirmed stays active and keeps its keys.
    /// </summary>
    private async Task<bool> StopDeactivateAndCleanupRevision(string revisionName)
    {
        if (!await _replicaStateCoordinator.SetDesiredStateAndWaitAsync(
            revisionName,
            WorkItemProcessorState.Stopped,
            requireAtLeastOneReplica: false))
        {
            _logger.LogError("Revision {revisionName} was not confirmed stopped and stays active", revisionName);
            return false;
        }

        await DeactivateRevision(revisionName);
        await _replicaStateCoordinator.DeleteStateAsync(revisionName);
        return true;
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
                revisionsTrafficWeight.FirstOrDefault(trafficWeight => trafficWeight.RevisionName == revision.Data.Name)?.Label))
            .ToList();

        foreach (var revision in revisionsToDeactivate)
        {
            if (!string.IsNullOrEmpty(revision.Label))
            {
                await RemoveRevisionLabel(revision.Name, revision.Label);
            }

            await StopDeactivateAndCleanupRevision(revision.Name);
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

    private async Task ActivateRevision(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        await revision.ActivateRevisionAsync();
        _logger.LogInformation("Activated revision {revisionName}", revisionName);
    }

    private async Task DeactivateRevision(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        await revision.DeactivateRevisionAsync();
        _logger.LogInformation("Deactivated revision {revisionName}", revisionName);
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

    private static bool HasAllTraffic(IEnumerable<ContainerAppRevisionTrafficWeight> trafficWeights, string revisionName)
        => trafficWeights.Any(weight => weight.Weight == 100
            && string.Equals(weight.RevisionName, revisionName, StringComparison.OrdinalIgnoreCase));

    private static int ToPollCount(int seconds) => (int)Math.Ceiling(seconds / (double)SleepTimeSeconds);

    private static string ConvertStringToCompressedBase64EncodedQuery(string query)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(query);
        using MemoryStream memoryStream = new();
        using GZipStream compressedStream = new(memoryStream, CompressionMode.Compress);

        compressedStream.Write(bytes, 0, bytes.Length);
        compressedStream.Close();
        memoryStream.Seek(0, SeekOrigin.Begin);
        var data = memoryStream.ToArray();
        var base64query = Convert.ToBase64String(data);
        return HttpUtility.UrlEncode(base64query);
    }
}
