// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octokit;
using Build = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using BuildData = Microsoft.DotNet.ProductConstructionService.Client.Models.BuildData;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool;

internal class ReproTool(
    IBarApiClient barClient,
    ReproToolOptions options,
    BuildAssetRegistryContext context,
    DarcProcessManager darcProcessManager,
    IProductConstructionServiceApi pcsApi,
    GitHubClient ghClient,
    ILogger<ReproTool> logger)
{
    private const string MaestroAuthTestOrgName = "maestro-auth-test";
    private const string VmrForkRepoName = "dotnet";
    private const string VmrForkUri = $"https://github.com/{MaestroAuthTestOrgName}/{VmrForkRepoName}";
    private const string ProductRepoFormat = $"https://github.com/{MaestroAuthTestOrgName}/";
    private const long InstallationId = 289474;
    private const string SourceMappingsPath = "src/source-mappings.json";
    private const string SourceManifestPath = "src/source-manifest.json";
    private const string DarcPRBranchPrefix = "darc-";

    internal async Task ReproduceCodeFlow()
    {
        logger.LogInformation("Fetching {subscriptionId} subscription from BAR",
            options.Subscription);
        var subscription = await barClient.GetSubscriptionAsync(options.Subscription);

        if (subscription == null)
        {
            throw new ArgumentException($"Couldn't find subscription with subscription id {options.Subscription}");
        }

        if (!subscription.SourceEnabled)
        {
            throw new ArgumentException($"Subscription {options.Subscription} is not a code flow subscription");
        }

        if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            throw new ArgumentException($"Code flow subscription incorrectly configured: is missing SourceDirectory or TargetDirectory");
        }

        await darcProcessManager.InitializeAsync();

        var defaultChannel = (await barClient.GetDefaultChannelsAsync(repository: subscription.SourceRepository, channel: subscription.Channel.Name)).First();

        string vmrBranch, productRepoUri, productRepoBranch;
        bool isForwardFlow;
        // Check if it's forward flow
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            vmrBranch = subscription.TargetBranch;
            productRepoUri = subscription.SourceRepository;
            productRepoBranch = defaultChannel.Branch;
            isForwardFlow = true;
        }
        // Backward flow
        else
        {
            vmrBranch = defaultChannel.Branch;
            productRepoUri = subscription.TargetRepository;
            productRepoBranch = subscription.TargetBranch;
            isForwardFlow = false;
        }
        var productRepoForkUri = ProductRepoFormat + productRepoUri.Split('/').Last();

        await using var vmrTmpBranch = await PrepareVmrForkAsync(vmrBranch, productRepoUri, productRepoForkUri);

        await PrepareProductRepoForkAsync(productRepoUri, productRepoForkUri, productRepoBranch);

        // Find the latest commit in the source repo to create a build from
        // TODO allow passing a specific commit of a repo. In that case check if it exists
        var sourceRepoUriParts = subscription.SourceRepository.Split("/");
        var res = await ghClient.Git.Reference.Get(sourceRepoUriParts[sourceRepoUriParts.Length - 2], sourceRepoUriParts[sourceRepoUriParts.Length - 1], $"heads/{defaultChannel.Branch}");
        var sourceRepoSha = res.Object.Sha;

        // Create the temp channel
        var channelName = Guid.NewGuid().ToString();
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName);

        // The build
        var build = await CreateBuildAsync(
            isForwardFlow ? productRepoForkUri : VmrForkUri,
            isForwardFlow ? defaultChannel.Branch : vmrTmpBranch.Value,
            sourceRepoSha);

        // And the subscription
        await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
            channel: channelName,
            sourceRepo: isForwardFlow ? productRepoForkUri : VmrForkUri,
            targetRepo: isForwardFlow ? VmrForkUri : productRepoForkUri,
            targetBranch: isForwardFlow ? vmrTmpBranch.Value : subscription.TargetBranch,
            sourceDirectory: subscription.SourceDirectory,
            targetDirectory: subscription.TargetDirectory);

        // Add it to the channel
        await darcProcessManager.AddBuildToChannelAsync(build.Id, channelName);

        // Trigger the subscription
        await TriggerSubscriptionAsync(testSubscription.Value);

        logger.LogInformation("Code flow successfully recreated. Press enter to finish and cleanup");
        Console.ReadLine();

        // Cleanup
        await DeleteAllDarcPRBranchesAsync(isForwardFlow ? VmrForkRepoName : productRepoUri.Split('/').Last());
    }

    private async Task DeleteAllDarcPRBranchesAsync(string repo)
    {
        var branches = await ghClient.Repository.Branch.GetAll(MaestroAuthTestOrgName, repo);
        foreach (var branch in branches.Where(b => b.Name.StartsWith(DarcPRBranchPrefix)))
        {
            await DeleteGitHubBranchAsync(repo, branch.Name);
        }
    }

    private async Task AddRepositoryToBarIfMissingAsync(string repositoryName)
    {
        if ((await context.Repositories.FirstOrDefaultAsync(repo => repo.RepositoryName == repositoryName)) == null)
        {
            context.Repositories.Add(new Maestro.Data.Models.Repository
            {
                RepositoryName = repositoryName,
                InstallationId = InstallationId
            });
            await context.SaveChangesAsync();
        }
    }

    private async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit)
    {
        Build build = await pcsApi.Builds.CreateAsync(new BuildData(
            commit: commit,
            azureDevOpsAccount: "test",
            azureDevOpsProject: "test",
            azureDevOpsBuildNumber: "1",
            azureDevOpsRepository: repositoryUrl,
            azureDevOpsBranch: branch,
            released: false,
            stable: false)
        {
            GitHubRepository = repositoryUrl,
            GitHubBranch = branch
        });

        return build;
    }

    private async Task TriggerSubscriptionAsync(string subscriptionId)
    {
        await pcsApi.Subscriptions.TriggerSubscriptionAsync(default, Guid.Parse(subscriptionId));
    }

    private async Task<AsyncDisposableValue<string>> PrepareVmrForkAsync(
        string branch,
        string productRepoUri,
        string productRepoForkUri)
    {
        // Sync the VMR fork branch
        await SyncForkAsync("dotnet", "dotnet", branch);
        // Check if the user has the forked VMR in local DB
        await AddRepositoryToBarIfMissingAsync(VmrForkUri);

        // Create a temporary branch
        var tmpBranch = Guid.NewGuid().ToString();
        var baseBranch = await ghClient.Git.Reference.Get(MaestroAuthTestOrgName, VmrForkRepoName, $"heads/{branch}");
        var newBranch = new NewReference($"refs/heads/{tmpBranch}", baseBranch.Object.Sha);
        var result = await ghClient.Git.Reference.Create(MaestroAuthTestOrgName, VmrForkRepoName, newBranch);


        // Fetch source mappings and source manifest files and replace the mapping for the repo we're testing on        
        await UpdateRemoteFileAsync(tmpBranch, productRepoUri, productRepoForkUri, SourceMappingsPath);
        await UpdateRemoteFileAsync(tmpBranch, productRepoUri, productRepoForkUri, SourceManifestPath);

        return AsyncDisposableValue.Create(tmpBranch, async () =>
        {
            logger.LogInformation("Cleaning up temporary branch {branchName}", tmpBranch);
            try
            {
                await DeleteGitHubBranchAsync(VmrForkRepoName, tmpBranch);
            }
            catch (Exception)
            {
                // If this throws an exception the most likely cause is that the branch was already deleted
            }
        });
    }

    private async Task DeleteGitHubBranchAsync(string repo, string branch) => await ghClient.Git.Reference.Delete(MaestroAuthTestOrgName, repo, $"heads/{branch}");

    private async Task UpdateRemoteFileAsync(string branch, string productRepoUri, string productRepoForkUri, string filePath)
    {
        // Fetch source mappings file and replace the mapping for the repo we're testing on        
        var sourceMappingsFile = (await ghClient.Repository.Content.GetAllContentsByRef(
            MaestroAuthTestOrgName,
            VmrForkRepoName,
            filePath,
            branch)).FirstOrDefault() ??
                throw new Exception($"Failed to find file {SourceMappingsPath} in {MaestroAuthTestOrgName}" +
                    $"/{VmrForkRepoName} on branch {SourceMappingsPath}");

        // Replace the product repo uri with the forked one
        var updatedSourceMappings = sourceMappingsFile.Content.Replace(productRepoUri, productRepoForkUri);
        UpdateFileRequest update = new(
            $"Update {productRepoUri} source mapping",
            updatedSourceMappings,
            sourceMappingsFile.Sha,
            branch);

        await ghClient.Repository.Content.UpdateFile(
            MaestroAuthTestOrgName,
            VmrForkRepoName,
            filePath,
            update);
    }

    private async Task PrepareProductRepoForkAsync(
        string productRepoUri,
        string productRepoForkUri,
        string productRepoBranch)
    {
        var parts = productRepoUri.Split('/');
        var org = parts[parts.Length - 2];
        var name = parts[parts.Length - 1];
        // Check if the product repo fork already exists
        var allRepos = await ghClient.Repository.GetAllForOrg(MaestroAuthTestOrgName);
        
        // If we already have a fork in maestro-auth-test, sync the branch we need with the source
        if (allRepos.FirstOrDefault(repo => repo.HtmlUrl == productRepoForkUri) != null)
        {
            await SyncForkAsync(org, name, productRepoBranch);
        }
        // If we don't, create a fork
        else
        {
            await ghClient.Repository.Forks.Create(org, name, new NewRepositoryFork { Organization = MaestroAuthTestOrgName });
        }
        await AddRepositoryToBarIfMissingAsync(productRepoForkUri);
    }

    private async Task SyncForkAsync(string originOwner, string originRepoName, string branch)
    {
        var reference = $"heads/{branch}";
        var upstream = await ghClient.Git.Reference.Get(originOwner, originRepoName, reference);
        await ghClient.Git.Reference.Update(MaestroAuthTestOrgName, originRepoName, reference, new ReferenceUpdate(upstream.Object.Sha));
    }
}
