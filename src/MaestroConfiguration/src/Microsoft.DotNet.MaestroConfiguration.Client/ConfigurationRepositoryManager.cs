// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public class ConfigurationRepositoryManager : IConfigurationRepositoryManager
{
    private readonly IGitRepoFactory _configurationRepoFactory;
    private readonly ILogger<IConfigurationRepositoryManager> _logger;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public ConfigurationRepositoryManager(
        IGitRepoFactory configurationRepoFactory,
        ILogger<IConfigurationRepositoryManager> logger)
    {
        _logger = logger;
        _configurationRepoFactory = configurationRepoFactory;
    }

    public async Task AddSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
    {
        IGitRepo configurationRepo = await _configurationRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        var newSubscriptionFilePath = string.IsNullOrEmpty(parameters.ConfigurationFilePath)
            ? ConfigFilePathResolver.GetDefaultSubscriptionFilePath(subscription)
            : parameters.ConfigurationFilePath;
        _logger.LogInformation("Adding new subscription to file {0}", newSubscriptionFilePath);

        var subscriptionsInFile = await FetchAndParseRemoteConfiguration<SubscriptionYaml>(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            newSubscriptionFilePath);

        // If we have a branch that hasn't been ingested yet, we need to check for equivalent subscriptions in the file
        var equivalentInFile = subscriptionsInFile.FirstOrDefault(s => s.IsEquivalentTo(subscription));
        if (equivalentInFile != null)
        {
            throw new ArgumentException($"Subscription {equivalentInFile.Id} with equivalent parameters already exists in '{newSubscriptionFilePath}'.");
        }

        subscriptionsInFile.Add(subscription);
        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            newSubscriptionFilePath,
            subscriptionsInFile,
            $"Add new subscription ({subscription.Channel}) {subscription.SourceRepository} => {subscription.TargetRepository} ({subscription.TargetBranch})");

        if (!parameters.DontOpenPr)
        {
            // Open a pull request for the new subscription
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                newSubscriptionFilePath,
                "Updating Maestro configuration");
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

    private static async Task CommitConfigurationDataAsync<T>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        string filePath,
        IEnumerable<T> data,
        string commitMessage)
        where T : IYamlModel
    {
        string yamlContent = _yamlSerializer.Serialize(YamlModelSorter.Sort(data)).Replace("\n-", "\n\n-");
        await gitRepo.CommitFilesAsync(repositoryUri, workingBranch, [new GitFile(filePath, yamlContent)], commitMessage);
    }

    /// <summary>
    /// Ensures that a configuration working branch exists, creating one if necessary.
    /// </summary>
    private static async Task<string> PrepareConfigurationBranchAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters parameters)
    {
        if (string.IsNullOrEmpty(parameters.ConfigurationBranch))
        {
            var branch = $"darc/{parameters.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            await gitRepo.CreateBranchAsync(
                parameters.RepositoryUri,
                branch,
                parameters.ConfigurationBaseBranch);
            return branch;
        }
        else
        {
            if (!await gitRepo.DoesBranchExistAsync(parameters.RepositoryUri, parameters.ConfigurationBranch))
            {
                await gitRepo.CreateBranchAsync(
                    parameters.RepositoryUri,
                    parameters.ConfigurationBranch,
                    parameters.ConfigurationBaseBranch);
            }
            return parameters.ConfigurationBranch;
        }
    }

    private static async Task<List<TData>> FetchAndParseRemoteConfiguration<TData>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        string filePath)
    {
        string fileContents;

        try
        {
            fileContents = await gitRepo.GetFileContentsAsync(
                repositoryUri,
                workingBranch,
                filePath);
            return _yamlDeserializer.Deserialize<List<TData>>(fileContents);
        }
        catch (FileNotFoundInRepoException)
        {
            return [];
        }   
    }

    private async Task CreatePullRequest(
        IGitRepo gitRepo,
        string repositoryUri,
        string headBranch,
        string targetBranch,
        string title,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(headBranch);
        ArgumentException.ThrowIfNullOrEmpty(targetBranch);

        _logger.LogInformation("Creating pull request from {0} to {1}...", headBranch, targetBranch);
        var prUrl = await gitRepo.CreatePullRequestAsync(
            repositoryUri,
            headBranch,
            targetBranch,
            title,
            description);
        var prId = prUrl.Substring(prUrl.LastIndexOf('/') + 1);
        var guiUri = $"{repositoryUri}/pullrequest/{prId}";
        _logger.LogInformation("Created pull request at {0}", guiUri);
    }

    public Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, Guid subscriptionId) => throw new NotImplementedException();
    public Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription) => throw new NotImplementedException();
}
