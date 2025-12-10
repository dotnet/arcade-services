// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.DarcLib.ConfigurationRepository;

public class ConfigurationRepositoryManager
{
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    protected readonly IRemoteFactory _remoteFactory;
    protected readonly IGitRepoFactory _gitRepoFactory;
    protected readonly ILogger<ConfigurationRepositoryManager> _logger;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public ConfigurationRepositoryManager(
        ILocalGitRepoFactory localGitRepoFactory,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitRepoFactory,
        ILogger<ConfigurationRepositoryManager> logger)
    {
        _localGitRepoFactory = localGitRepoFactory;
        _remoteFactory = remoteFactory;
        _gitRepoFactory = gitRepoFactory;
        _logger = logger;
    }

    public async Task AddSubsciptionAsync(
        ConfigurationRepositoryOperationParameters parameters,
        SubscriptionYaml subscription,
        string? overrideFilePath = null)
    {
        IGitRepo gitRepo = _gitRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(gitRepo, parameters);
        await EnsureConfigurationWorkingBranchAsync(gitRepo, parameters);

        var newSubscriptionFilePath = string.IsNullOrEmpty(overrideFilePath)
            ? MaestroConfigHelper.GetDefaultSubscriptionFilePath(subscription)
            : new UnixPath(overrideFilePath);

        var subscriptionsInFile = await FetchAndParseRemoteConfiguration<SubscriptionYaml>(gitRepo, parameters, newSubscriptionFilePath);

        // If we have a branch that hasn't been ingested yet, we need to check for equivalent subscriptions in the file
        var equivalentInFile = subscriptionsInFile.FirstOrDefault(s => s.IsEquivalentTo(subscription));
        if (equivalentInFile != null)
        {
            throw new ArgumentException($"Subscription {equivalentInFile.Id} with equivalent parameters already exists in '{newSubscriptionFilePath}'.");
        }

        subscriptionsInFile.Add(subscription);
        await WriteConfigurationDataAsync(
            gitRepo,
            parameters,
            newSubscriptionFilePath,
            subscriptionsInFile.Order(),
            $"Add new subscription ({subscription.Channel}) {subscription.SourceRepository} => {subscription.TargetRepository} ({subscription.TargetBranch})");

        if (!parameters.DontOpenPr)
        {
            await CreatePullRequest(gitRepo, parameters, "Update Maestro configuration");
        }
        else
        {
            _logger.LogInformation("Successfully added subscription with id '{0}' to branch '{1}' of the configuration repository {2}",
                subscription.Id, parameters.ConfigurationBranch, parameters.RepositoryUri);
        }
    }

    private static async Task ValidateConfigurationRepositoryParametersAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters operationParameters)
    {
        if (!await gitRepo.RepoExistsAsync(operationParameters.RepositoryUri))
        {
            throw new ArgumentException($"The configuration repository '{operationParameters.RepositoryUri}' is not a valid git repository.");
        }

        if (!await gitRepo.DoesBranchExistAsync(operationParameters.RepositoryUri, operationParameters.ConfigurationBaseBranch))
        {
            throw new ArgumentException($"The configuration base branch '{operationParameters.ConfigurationBaseBranch}' does not exist in the repository '{operationParameters.RepositoryUri}'.");
        }
    }

    private async Task WriteConfigurationDataAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters parameters,
        string filePath,
        IEnumerable<object> data,
        string commitMessage)
    {
        string yamlContents = _yamlSerializer.Serialize(data).Replace("\n-", "\n\n-");

        if (gitRepo is LocalLibGit2Client)
        {
            GitFile fileToCommit = new(new NativePath(parameters.RepositoryUri) / filePath, yamlContents);
            await gitRepo.CommitFilesAsync(
                [fileToCommit],
                parameters.RepositoryUri,
                parameters.ConfigurationBranch,
                commitMessage);
            var local = _localGitRepoFactory.Create(new NativePath(parameters.RepositoryUri));
            await local.StageAsync(["."]);
            await local.CommitAsync(commitMessage, allowEmpty: false);
        }
        else
        {
            GitFile fileToCommit = new(filePath, yamlContents);
            var remote = await _remoteFactory.CreateRemoteAsync(parameters.RepositoryUri);
            await remote.CommitUpdatesWithNoCloningAsync(
                [fileToCommit],
                parameters.RepositoryUri,
                parameters.ConfigurationBranch,
                commitMessage);
        }
    }

    /// <summary>
    /// Ensures that a configuration working branch exists, creating one if necessary.
    /// </summary>
    private static async Task EnsureConfigurationWorkingBranchAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters operationParameters)
    {
        if (string.IsNullOrEmpty(operationParameters.ConfigurationBranch))
        {
            var branch = $"darc/{operationParameters.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            await gitRepo.CreateBranchAsync(
                operationParameters.RepositoryUri,
                branch,
                operationParameters.ConfigurationBaseBranch);
            operationParameters.ConfigurationBranch = branch    ;
        }
        else
        {
            if (!await gitRepo.DoesBranchExistAsync(operationParameters.RepositoryUri, operationParameters.ConfigurationBranch))
            {
                await gitRepo.CreateBranchAsync(
                    operationParameters.RepositoryUri,
                    operationParameters.ConfigurationBranch,
                    operationParameters.ConfigurationBaseBranch);
            }
        }
    }

    private static async Task<List<TData>> FetchAndParseRemoteConfiguration<TData>(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters operationParameters,
        string filePath)
    {
        string fileContents;
        try
        {
            fileContents = await gitRepo.GetFileContentsAsync(
                filePath,
                operationParameters.RepositoryUri,
                operationParameters.ConfigurationBranch);
            return _yamlDeserializer.Deserialize<List<TData>>(fileContents);
        }
        catch (DependencyFileNotFoundException)
        {
            return [];
        }
    }

    private async Task CreatePullRequest(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters parameters,
        string title,
        string? description = null)
    {
        if (gitRepo is LocalLibGit2Client)
        {
            throw new InvalidOperationException("Cannot create pull request when using local git repository. Specify a remote repository as the configuration repository");
        }
        if (string.IsNullOrEmpty(parameters.ConfigurationBaseBranch))
        {
            throw new InvalidOperationException("Cannot create pull request without a configuration base branch specified");
        }

        _logger.LogInformation("Creating pull request from {0} to {1}...", parameters.ConfigurationBranch, parameters.ConfigurationBaseBranch);
        var remote = await _remoteFactory.CreateRemoteAsync(parameters.RepositoryUri);
        var pr = await remote.CreatePullRequestAsync(parameters.RepositoryUri, new PullRequest()
        {
            HeadBranch = parameters.ConfigurationBranch,
            BaseBranch = parameters.ConfigurationBaseBranch,
            Title = title,
            Description = description ?? string.Empty,
        });
        var prId = pr.Url.Substring(pr.Url.LastIndexOf('/') + 1);
        var guiUri = $"{parameters.RepositoryUri}/pullrequest/{prId}";
        _logger.LogInformation("Created pull request at {0}", guiUri);
    }
}
