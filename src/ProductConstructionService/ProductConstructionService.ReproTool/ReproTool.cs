// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octokit;
using Build = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using BuildData = Microsoft.DotNet.ProductConstructionService.Client.Models.BuildData;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool;

internal class ReproTool(
    IBarApiClient prodBarClient,
    ReproToolOptions options,
    BuildAssetRegistryContext context,
    DarcProcessManager darcProcessManager,
    IProductConstructionServiceApi localPcsApi,
    GitHubClient ghClient,
    ILogger<ReproTool> logger)
{
    private const string MaestroAuthTestOrgName = "maestro-auth-test";
    private const string VmrForkRepoName = "dotnet";
    private const string VmrForkUri = $"https://github.com/{MaestroAuthTestOrgName}/{VmrForkRepoName}";
    private const string ProductRepoFormat = $"https://github.com/{MaestroAuthTestOrgName}/";
    private const long InstallationId = 289474;
    private const string SourceMappingsPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceMappingsFileName}";
    private const string SourceManifestPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceManifestFileName}";
    private const string DarcPRBranchPrefix = "darc";

    internal async Task ReproduceCodeFlow()
    {
        logger.LogInformation("Fetching {subscriptionId} subscription from BAR",
            options.Subscription);
        var subscription = await prodBarClient.GetSubscriptionAsync(options.Subscription);

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
            throw new ArgumentException("Code flow subscription incorrectly configured: is missing SourceDirectory or TargetDirectory");
        }

        if (!string.IsNullOrEmpty(options.Commit) && options.BuildId != null)
        {
            throw new ArgumentException($"Only one of {nameof(ReproToolOptions.Commit)} and {nameof(ReproToolOptions.BuildId)} can be provided");
        }

        Build? build = null;
        if (options.BuildId != null)
        {
            build = await prodBarClient.GetBuildAsync(options.BuildId.Value);
            if (build.GitHubRepository != subscription.SourceRepository)
            {
                throw new ArgumentException($"Build {build.Id} repository {build.GitHubRepository} doesn't match the subscription source repository {subscription.SourceRepository}");
            }
        }
        await darcProcessManager.InitializeAsync();

        var defaultChannel = (await prodBarClient.GetDefaultChannelsAsync(repository: subscription.SourceRepository, channel: subscription.Channel.Name)).First();

        string vmrBranch, productRepoUri, productRepoBranch;
        bool isForwardFlow = !string.IsNullOrEmpty(subscription.TargetDirectory);
        if (isForwardFlow)
        {
            vmrBranch = subscription.TargetBranch;
            productRepoUri = subscription.SourceRepository;
            productRepoBranch = defaultChannel.Branch;
        }
        else
        {
            vmrBranch = defaultChannel.Branch;
            productRepoUri = subscription.TargetRepository;
            productRepoBranch = subscription.TargetBranch;
        }
        var productRepoForkUri = ProductRepoFormat + productRepoUri.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        logger.LogInformation("Reproducing subscription from {sourceRepo} to {targetRepo}",
            isForwardFlow ? productRepoForkUri : VmrForkUri,
            isForwardFlow ? VmrForkUri : productRepoForkUri);

        await using var vmrTmpBranch = await PrepareVmrForkAsync(vmrBranch, productRepoUri, productRepoForkUri, options.SkipCleanup);

        logger.LogInformation("Preparing product repo fork {productRepoFork}, branch {branch}", productRepoForkUri, productRepoBranch);
        await using var productRepoTmpBranch = await PrepareProductRepoForkAsync(productRepoUri, productRepoForkUri, productRepoBranch, options.SkipCleanup);

        // Find the latest commit in the source repo to create a build from
        string sourceRepoSha;
        (string sourceRepoName, string sourceRepoOwner) = GitRepoUrlParser.GetRepoNameAndOwner(subscription.SourceRepository);
        if (build != null)
        {
            sourceRepoSha = build.Commit;
        }
        else if (string.IsNullOrEmpty(options.Commit))
        {
            var res = await ghClient.Git.Reference.Get(sourceRepoOwner, sourceRepoName, $"heads/{defaultChannel.Branch}");
            sourceRepoSha = res.Object.Sha;
        }
        else
        {
            // Validate that the commit actually exists
            try
            {
                await ghClient.Repository.Commit.Get(sourceRepoOwner, sourceRepoName, options.Commit);
            }
            catch (NotFoundException)
            {
                throw new ArgumentException($"Commit {options.Commit} doesn't exist in repo {subscription.SourceRepository}");
            }
            sourceRepoSha = options.Commit;
        }

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName, options.SkipCleanup);

        var testBuild = await CreateBuildAsync(
            isForwardFlow ? productRepoForkUri : VmrForkUri,
            isForwardFlow ? productRepoTmpBranch.Value : vmrTmpBranch.Value,
            sourceRepoSha,
            build != null ? CreateAssetDataFromBuild(build) : []);

        await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
            channel: channelName,
            sourceRepo: isForwardFlow ? productRepoForkUri : VmrForkUri,
            targetRepo: isForwardFlow ? VmrForkUri : productRepoForkUri,
            targetBranch: isForwardFlow ? vmrTmpBranch.Value : productRepoTmpBranch.Value,
            sourceDirectory: subscription.SourceDirectory,
            targetDirectory: subscription.TargetDirectory,
            skipCleanup: options.SkipCleanup);

        await darcProcessManager.AddBuildToChannelAsync(testBuild.Id, channelName, options.SkipCleanup);

        await TriggerSubscriptionAsync(testSubscription.Value);

        logger.LogInformation("Code flow successfully recreated. Press enter to finish and cleanup");
        Console.ReadLine();

        if (options.SkipCleanup)
        {
            logger.LogInformation("Skipping cleanup. If you want to re-trigger the reproduced subscription run \"darc trigger-subscriptions --ids {subscriptionId}\"", testSubscription.Value);
        }
        else
        {
            // Cleanup
            await DeleteDarcPRBranchAsync(
                isForwardFlow ? VmrForkRepoName : productRepoUri.Split('/').Last(),
                isForwardFlow ? vmrTmpBranch.Value : productRepoTmpBranch.Value);
        }
    }

    private async Task DeleteDarcPRBranchAsync(string repo, string targetBranch)
    {
        var branch = (await ghClient.Repository.Branch.GetAll(MaestroAuthTestOrgName, repo))
            .FirstOrDefault(branch => branch.Name.StartsWith($"{DarcPRBranchPrefix}-{targetBranch}"))
            ?? throw new Exception($"Couldn't find darc PR branch targeting branch {targetBranch}");
        await DeleteGitHubBranchAsync(repo, branch.Name);
    }

    private async Task AddRepositoryToBarIfMissingAsync(string repositoryName)
    {
        if ((await context.Repositories.FirstOrDefaultAsync(repo => repo.RepositoryName == repositoryName)) == null)
        {
            logger.LogInformation("Repo {repo} missing in local BAR. Adding an entry for it", repositoryName);
            context.Repositories.Add(new Maestro.Data.Models.Repository
            {
                RepositoryName = repositoryName,
                InstallationId = InstallationId
            });
            await context.SaveChangesAsync();
        }
    }

    private async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, List<AssetData> assets)
    {
        logger.LogInformation("Creating a test build");

        Build build = await localPcsApi.Builds.CreateAsync(new BuildData(
            commit: commit,
            azureDevOpsAccount: "test",
            azureDevOpsProject: "test",
            azureDevOpsBuildNumber: $"{DateTime.UtcNow.ToString("yyyyMMdd")}.{new Random().Next(1, 75)}",
            azureDevOpsRepository: repositoryUrl,
            azureDevOpsBranch: branch,
            released: false,
            stable: false)
        {
            GitHubRepository = repositoryUrl,
            GitHubBranch = branch,
            Assets = assets
        });

        return build;
    }

    private List<AssetData> CreateAssetDataFromBuild(Build build)
    {
        return build.Assets.Select(asset => new AssetData(false)
        {
            Name = asset.Name,
            Version = asset.Version,
            Locations = asset.Locations.Select(location => new AssetLocationData(location.Type) { Location = location.Location}).ToList()
        }).ToList();
    }

    private async Task TriggerSubscriptionAsync(string subscriptionId)
    {
        logger.LogInformation("Triggering subscription {subscriptionId}", subscriptionId);
        await localPcsApi.Subscriptions.TriggerSubscriptionAsync(default, Guid.Parse(subscriptionId));
    }

    private async Task<AsyncDisposableValue<string>> PrepareVmrForkAsync(
        string branch,
        string productRepoUri,
        string productRepoForkUri,
        bool skipCleanup)
    {
        logger.LogInformation("Preparing VMR fork");
        // Sync the VMR fork branch
        await SyncForkAsync("dotnet", "dotnet", branch);
        // Check if the user has the forked VMR in local DB
        await AddRepositoryToBarIfMissingAsync(VmrForkUri);

        var newBranch = await CreateTmpBranchAsync(VmrForkRepoName, branch, skipCleanup);

        // Fetch source mappings and source manifest files and replace the mapping for the repo we're testing on
        logger.LogInformation("Updating source mappings and source manifest files in VMR fork to replace original product repo mapping with fork mapping");
        await UpdateRemoteVmrForkFileAsync(newBranch.Value, productRepoUri, productRepoForkUri, SourceMappingsPath);
        await UpdateRemoteVmrForkFileAsync(newBranch.Value, productRepoUri, productRepoForkUri, SourceManifestPath);

        return newBranch;
    }

    private async Task DeleteGitHubBranchAsync(string repo, string branch) => await ghClient.Git.Reference.Delete(MaestroAuthTestOrgName, repo, $"heads/{branch}");

    private async Task UpdateRemoteVmrForkFileAsync(string branch, string productRepoUri, string productRepoForkUri, string filePath)
    {
        logger.LogInformation("Updating file {file} on branch {branch} in the VMR fork", filePath, branch);
        // Fetch remote file and replace the product repo URI with the repo we're testing on        
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

    private async Task<AsyncDisposableValue<string>> PrepareProductRepoForkAsync(
        string productRepoUri,
        string productRepoForkUri,
        string productRepoBranch,
        bool skipCleanup)
    {
        logger.LogInformation("Preparing product repo {repo} fork", productRepoUri);
        (var name, var org) = GitRepoUrlParser.GetRepoNameAndOwner(productRepoUri);
        // Check if the product repo fork already exists
        var allRepos = await ghClient.Repository.GetAllForOrg(MaestroAuthTestOrgName);
        
        // If we already have a fork in maestro-auth-test, sync the branch we need with the source
        if (allRepos.FirstOrDefault(repo => repo.HtmlUrl == productRepoForkUri) != null)
        {
            logger.LogInformation("Product repo fork {fork} already exists, syncing branch {branch} with source", productRepoForkUri, productRepoBranch);
            await SyncForkAsync(org, name, productRepoBranch);
        }
        // If we don't, create a fork
        else
        {
            logger.LogInformation("Forking product repo {source} to fork {fork}", productRepoUri, productRepoForkUri);
            await ghClient.Repository.Forks.Create(org, name, new NewRepositoryFork { Organization = MaestroAuthTestOrgName });
        }
        await AddRepositoryToBarIfMissingAsync(productRepoForkUri);

        return await CreateTmpBranchAsync(name, productRepoBranch, skipCleanup);
    }

    private async Task SyncForkAsync(string originOrg, string repoName, string branch)
    {
        logger.LogInformation("Syncing fork {fork} branch {branch} with upstream repo {upstream}", $"{MaestroAuthTestOrgName}/{repoName}", branch, $"{originOrg}/{repoName}");
        var reference = $"heads/{branch}";
        var upstream = await ghClient.Git.Reference.Get(originOrg, repoName, reference);
        await ghClient.Git.Reference.Update(MaestroAuthTestOrgName, repoName, reference, new ReferenceUpdate(upstream.Object.Sha, true));
    }

    private async Task<AsyncDisposableValue<string>> CreateTmpBranchAsync(string repoName, string originalBranch, bool skipCleanup)
    {
        var newBranchName = $"repro/{Guid.NewGuid().ToString()}";
        logger.LogInformation("Creating temporary branch {branch} in {repo}", newBranchName, $"{MaestroAuthTestOrgName}/{repoName}");

        var baseBranch = await ghClient.Git.Reference.Get(MaestroAuthTestOrgName, repoName, $"heads/{originalBranch}");
        var newBranch = new NewReference($"refs/heads/{newBranchName}", baseBranch.Object.Sha);
        await ghClient.Git.Reference.Create(MaestroAuthTestOrgName, repoName, newBranch);

        return AsyncDisposableValue.Create(newBranchName, async () =>
        {
            if (skipCleanup)
            {
                return;
            }

            logger.LogInformation("Cleaning up temporary branch {branchName}", newBranchName);
            try
            {
                await DeleteGitHubBranchAsync(repoName, newBranchName);
            }
            catch
            {
                // If this throws an exception the most likely cause is that the branch was already deleted
            }
        });
    }
}
