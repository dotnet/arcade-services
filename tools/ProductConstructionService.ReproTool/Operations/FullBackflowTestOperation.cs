// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.ReproTool.Options;
using Tools.Common;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool.Operations;
internal class FullBackflowTestOperation : Operation
{
    private readonly IBarApiClient _prodBarClient;
    private readonly FullBackflowTestOptions _options;
    private readonly DarcProcessManager _darcProcessManager;
    private readonly VmrDependencyResolver _vmrDependencyResolver;

    public FullBackflowTestOperation(
        ILogger<Operation> logger,
        GitHubClient ghClient,
        [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi,
        IBarApiClient prodBarClient,
        FullBackflowTestOptions options,
        DarcProcessManager darcProcessManager,
        VmrDependencyResolver vmrDependencyResolver)
        : base(logger, ghClient, localPcsApi)
    {
        _prodBarClient = prodBarClient;
        _options = options;
        _darcProcessManager = darcProcessManager;
        _vmrDependencyResolver = vmrDependencyResolver;
    }

    internal override async Task RunAsync()
    {
        await _darcProcessManager.InitializeAsync();
        Build vmrBuild = await _prodBarClient.GetBuildAsync(_options.BuildId);

        Build testBuild = await CreateBuildAsync(
            VmrForkUri,
            _options.VmrBranch,
            _options.Commit,
            [ ..CreateAssetDataFromBuild(vmrBuild).Take(1000)]);

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await _darcProcessManager.CreateTestChannelAsync(channelName, skipCleanup: true);
        await _darcProcessManager.AddBuildToChannelAsync(testBuild.Id, channelName, skipCleanup: true);

        var vmrRepos = (await _vmrDependencyResolver.GetVmrRepositoriesAsync(
            "https://github.com/dotnet/dotnet",
            "https://github.com/dotnet/sdk",
            "main"));

        foreach (var vmrRepo in vmrRepos)
        {
            var productRepoForkUri = $"{ProductRepoFormat}{vmrRepo.Mapping.DefaultRemote.Split('/', StringSplitOptions.RemoveEmptyEntries).Last()}";

            var subscription = await _darcProcessManager.CreateSubscriptionAsync(
                channel: channelName,
                sourceRepo: VmrForkUri,
                targetRepo: productRepoForkUri,
                targetBranch: _options.TargetBranch,
                sourceDirectory: vmrRepo.Mapping.Name,
                targetDirectory: null,
                skipCleanup: true);

            await _darcProcessManager.TriggerSubscriptionAsync(subscription.Value);
        }
    }
}
