// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
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
            new SubscriptionYamlComparer(),
            $"Add new subscription ({subscription.Channel}) {subscription.SourceRepository} => {subscription.TargetRepository} ({subscription.TargetBranch})");;

        if (!parameters.DontOpenPr)
        {
            // Open a pull request for the new subscription
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                "Updating Maestro configuration");
        }
        else
        {
            _logger.LogInformation("Successfully added subscription with id '{0}' to branch '{1}' of the configuration repository {2}",
                subscription.Id, parameters.ConfigurationBranch, parameters.RepositoryUri);
        }
    }

    public async Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
    {
        IGitRepo configurationRepo = await _configurationRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        string subscriptionFilePath;
        List<SubscriptionYaml> subscriptionsInFile;
        if (string.IsNullOrEmpty(parameters.ConfigurationFilePath))
        {
            (subscriptionFilePath, subscriptionsInFile) = await FindAndParseConfigurationFile(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                subscription,
                YamlModelUniqueKeys.GetSubscriptionKey);
        }
        else
        {
            subscriptionFilePath = parameters.ConfigurationFilePath;
            subscriptionsInFile = await FetchAndParseRemoteConfiguration<SubscriptionYaml>(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                subscriptionFilePath);
        }

        var subscriptionsWithoutDeleted = subscriptionsInFile.Where(s => s.Id != subscription.Id).ToList();

        if (subscriptionsInFile.Count == subscriptionsWithoutDeleted.Count)
        {
            throw new ArgumentException(
                $"Found no subscription with id {subscription.Id} to delete in file {subscriptionFilePath} " +
                $"of repo {parameters.RepositoryUri} on branch {parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch}");
        }

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            subscriptionFilePath,
            subscriptionsWithoutDeleted,
            new SubscriptionYamlComparer(),
            $"Delete subscription {subscription.Id}");

        if (!parameters.DontOpenPr)
        {
            // Open a pull request for the new subscription
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                "Updating Maestro configuration");
        }
        else
        {
            _logger.LogInformation("Successfully deleted subscription with id '{0}' from branch '{1}' of the configuration repository {2}",
                subscription.Id, parameters.ConfigurationBranch, parameters.RepositoryUri);
        }
    }

    public async Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription)
    {
        var configurationRepo = await _configurationRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        string subscriptionFilePath;
        List<SubscriptionYaml> subscriptionsInFile;
        if (string.IsNullOrEmpty(parameters.ConfigurationFilePath))
        {
            (subscriptionFilePath, subscriptionsInFile) = await FindAndParseConfigurationFile(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                updatedSubscription,
                YamlModelUniqueKeys.GetSubscriptionKey);
        }
        else
        {
            subscriptionFilePath = parameters.ConfigurationFilePath;
            subscriptionsInFile = await FetchAndParseRemoteConfiguration<SubscriptionYaml>(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                subscriptionFilePath);
        }

        // delete the old subscription (with the same id) and add the updated one
        var existingSubscription = subscriptionsInFile.FirstOrDefault(s => s.Id == updatedSubscription.Id);
        if (existingSubscription == null)
        {
            throw new ArgumentException(
                $"No existing subscription with id {updatedSubscription.Id} found in file {subscriptionFilePath} " +
                $"of repo {parameters.RepositoryUri} on branch {parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch}");
        }
        subscriptionsInFile.Remove(existingSubscription);
        subscriptionsInFile.Add(updatedSubscription);

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            subscriptionFilePath,
            subscriptionsInFile,
            new SubscriptionYamlComparer(),
            $"Update subscription {updatedSubscription.Id}");

        if (!parameters.DontOpenPr)
        {
            // Open a pull request for the new subscription
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                "Updating Maestro configuration");
        }
        else
        {
            _logger.LogInformation("Successfully updated subscription with id '{0}' on branch '{1}' of the configuration repository {2}",
                updatedSubscription.Id, parameters.ConfigurationBranch, parameters.RepositoryUri);
        }
    }


    #region helper methods
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
        IComparer<T> comparer,
        string commitMessage)
        where T : IYamlModel
    {
        if (!data.Any())
        {
            await gitRepo.DeleteFileAsync(repositoryUri, workingBranch, filePath, commitMessage);
        }
        else
        {
            string yamlContent = _yamlSerializer.Serialize(data.OrderBy(x => x, comparer)).Replace("\n-", "\n\n-");
            await gitRepo.CommitFilesAsync(repositoryUri, workingBranch, [new GitFile(filePath, yamlContent)], commitMessage);
        }
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

    private async Task<(string, List<T>)> FindAndParseConfigurationFile<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        // Try the default file first before searching all files
        _logger.LogInformation("No configuration file path provided. Trying default location first...");
        var result = await TryFindInDefaultFileAsync(gitRepo, repositoryUri, workingBranch, searchObject, getUniqueKey);
        if (result.HasValue)
        {
            return result.Value;
        }

        // If not in the default file, search all files in the folder for that yaml type
        _logger.LogInformation("Couldn't find configuration object at the default location. Searching all files in folder... this might take a few minutes");
        return await SearchAllFilesInFolderAsync(gitRepo, repositoryUri, workingBranch, searchObject, getUniqueKey);
    }

    private async Task<(string FilePath, List<T> Content)?> TryFindInDefaultFileAsync<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var defaultFilePath = ConfigFilePathResolver.GetDefaultFilePath(searchObject);
        var searchKey = getUniqueKey(searchObject);

        try
        {
            var defaultFileContents = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, defaultFilePath);
            var deserializedYamls = _yamlDeserializer.Deserialize<List<T>>(defaultFileContents);

            if (deserializedYamls.Any(y => searchKey.Equals(getUniqueKey(y))))
            {
                return (defaultFilePath, deserializedYamls);
            }
        }
        catch (FileNotFoundInRepoException)
        {
            // Default file doesn't exist
        }

        return null;
    }

    private async Task<(string FilePath, List<T> Content)> SearchAllFilesInFolderAsync<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var folderPath = ConfigFilePathResolver.GetDefaultFileFolder(searchObject);
        var searchKey = getUniqueKey(searchObject);

        // Get list of all files in the folder
        var filePaths = await gitRepo.ListBlobsAsync(repositoryUri, workingBranch, folderPath);

        // Search each file one by one for the object
        foreach (var filePath in filePaths)
        {
            try
            {
                var fileContent = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, filePath);
                var deserializedYamls = _yamlDeserializer.Deserialize<List<T>>(fileContent);

                if (deserializedYamls.Any(y => searchKey.Equals(getUniqueKey(y))))
                {
                    _logger.LogInformation("Object with key {0} found in {1}", searchKey, filePath);
                    return (filePath, deserializedYamls);
                }
            }
            catch (FileNotFoundInRepoException)
            {
                // File was listed but couldn't be read, skip it
                continue;
            }
        }

        throw new ArgumentException($"No object with key {searchKey} was found on branch {workingBranch}");
    }
    #endregion
}
