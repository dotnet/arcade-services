// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Client;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;
using StackExchange.Redis;

namespace ProductConstructionService.Deployment;
public class Deployer
{
    private readonly DeploymentOptions _options;
    private ContainerAppResource _containerApp;
    private readonly ResourceGroupResource _resourceGroup;
    private readonly IProcessManager _processManager;
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly DefaultAzureCredential _credential;

    private const int SleepTimeSeconds = 20;

    public Deployer(
        DeploymentOptions options,
        IProcessManager processManager,
        IProductConstructionServiceApi pcsClient)
    {
        _options = options;
        _processManager = processManager;
        _pcsClient = pcsClient;

        _credential = new();
        ArmClient client = new(_credential);
        SubscriptionResource subscription = client.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_options.SubscriptionId}"));

        _resourceGroup = subscription.GetResourceGroups().Get("product-construction-service");
        _containerApp = _resourceGroup.GetContainerApp("product-construction-int").Value;
    }

    private string[] DefaultAzCliParameters => [
        "--name", _options.ContainerAppName,
        "--resource-group", _options.ResourceGroupName,
        ];
    private readonly RevisionRunningState RunningAtMaxScaleState = new RevisionRunningState("RunningAtMaxScale");

    public async Task<int> DeployAsync()
    {
        List<ContainerAppRevisionTrafficWeight> trafficWeights = _containerApp.Data.Configuration.Ingress.Traffic.ToList();

        var activeRevisionTrafficWeight = trafficWeights.FirstOrDefault(weight => weight.Weight == 100) ??
            throw new ArgumentException("Container app has no active revision, please investigate manually");
        var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(activeRevisionTrafficWeight.RevisionName)).Value;
        var replicas = activeRevision.GetContainerAppReplicas().ToList();

        Console.WriteLine($"Currently active revision {activeRevisionTrafficWeight.RevisionName} with label {activeRevisionTrafficWeight.Label}");

        // Determine the label of the inactive revision
        string inactiveRevisionLabel = activeRevisionTrafficWeight.Label == "blue" ? "green" : "blue";

        Console.WriteLine($"Next revision will be deployed with label {inactiveRevisionLabel}");
        Console.WriteLine($"Removing label {inactiveRevisionLabel} from inactive revision");

        // Cleanup all revisions except the currently active one
        await CleanupRevisionsAsync(trafficWeights.Where(weight => weight != activeRevisionTrafficWeight));

        // Tell the active revision to finish current work items and stop processing new ones
        await StopProcessingNewJobs(activeRevisionTrafficWeight.RevisionName);

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
                await DeactivateFailedRevisionAndGetLogs(newRevisionName);
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
            Console.WriteLine("Starting the service again");
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
        while (await Utility.SleepIfTrue(
            () => status != RunningAtMaxScaleState && status != RevisionRunningState.Failed,
            SleepTimeSeconds));

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

    private async Task DeactivateFailedRevisionAndGetLogs(string revisionName)
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

        var encodedQuery = Utility.ConvertStringToCompressedBase64EncodedQuery(query);

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

    private async Task StopProcessingNewJobs(string activeRevisionName)
    {
        Console.WriteLine("Stopping the service from processing new jobs");
        await _pcsClient.Status.StopPcsWorkItemProcessorAsync();

        string status;
        try
        {
            do
            {
                status = await _pcsClient.Status.GetPcsWorkItemProcessorStatusAsync();

                Console.WriteLine($"Current status: {status}");
            } while (await Utility.SleepIfTrue(() => status != "Stopped", SleepTimeSeconds));
        }
        catch(Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex}. Deploying the new revision without stopping the service");
        }
    }

    private async Task<List<WorkItemProcessorState>> GetRevisionReplicaStates(string revisionName)
    {
        var redisConfig = ConfigurationOptions.Parse(_options.RedisConnectionString);
        await redisConfig.ConfigureForAzureWithTokenCredentialAsync(_credential);
        RedisCacheFactory redisCacheFactory = new(redisConfig, LoggerFactory.Create(config => config.AddConsole()).CreateLogger<RedisCache>());

        var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        return activeRevision.GetContainerAppReplicas()
            // Without this, VS can't distinguish between Enumerable and AsyncEnumerable in the Select bellow
            .ToEnumerable()
            .Select(replica => new WorkItemProcessorState(redisCacheFactory, replica.Data.Name))
            .ToList();
    }

    private async Task StartActiveRevision()
    {
        // refresh the containerApp resource
        _containerApp = await _containerApp.GetAsync();

        // Get the name of the currently active revision
        var activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(trafficWeight => trafficWeight.Weight == 100);

        Console.WriteLine($"Starting all replicas of the {activeRevisionTrafficWeight.RevisionName} revision");
        var replicaStates = await GetRevisionReplicaStates(activeRevisionTrafficWeight.RevisionName);
        var tasks = replicaStates.Select(replicaState => replicaState.StartAsync()).ToArray();

        Task.WaitAll(tasks);
    }
}
