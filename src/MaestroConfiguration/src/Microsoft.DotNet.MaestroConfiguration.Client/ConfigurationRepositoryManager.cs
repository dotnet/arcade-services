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
        try
        {
            await PerformConfigurationRepositoryOperationInternal(
                parameters,
                subscription,
                (p, repo, branch, s) => AddModelInternalAsync<SubscriptionYaml, Guid>(
                    p, repo, branch, s,
                    ConfigFilePathResolver.GetDefaultSubscriptionFilePath,
                    (existing, newSub) => existing.IsEquivalentTo(newSub),
                    new SubscriptionYamlComparer(),
                    $"Add new subscription ({s.Channel}) {s.SourceRepository} => {s.TargetRepository} ({s.TargetBranch})"),
                $"Successfully added subscription with id '{subscription.Id}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");
        }
        catch (DuplicateConfigurationObjectException ex)
        {
            _logger.LogError("Subscription {id} with equivalent parameters already exists in '{filePath}'.",
                subscription.Id,
                ex.FilePath);
            throw;
        }
    }
    public async Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
    {
        try
        {
            await PerformConfigurationRepositoryOperationInternal(
                parameters,
                subscription,
                (p, repo, branch, s) => DeleteModelInternalAsync(
                    p, repo, branch, s,
                    YamlModelUniqueKeys.GetSubscriptionKey,
                    new SubscriptionYamlComparer(),
                    $"Delete subscription {s.Id}"),
                $"Successfully deleted subscription with id '{subscription.Id}' from branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");
        }
        catch (ConfigurationObjectNotFoundException ex)
        {
            _logger.LogError("No existing subscription with id {id} found in file {filePath} of repo {repo} on branch {branch}",
                subscription.Id,
                ex.FilePath,
                ex.RepositoryUri,
                ex.BranchName);
            throw;
        }
    }
    public async Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription)
    {
        try
        {
            await PerformConfigurationRepositoryOperationInternal(
                parameters,
                updatedSubscription,
                (p, repo, branch, s) => UpdateModelInternalAsync(
                    p, repo, branch, s,
                    YamlModelUniqueKeys.GetSubscriptionKey,
                    new SubscriptionYamlComparer(),
                    $"Update subscription {s.Id}"),
                $"Successfully updated subscription with id '{updatedSubscription.Id}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");
        }
        catch (ConfigurationObjectNotFoundException ex)
        {
            _logger.LogError("No existing subscription with id {id} found in file {filePath} of repo {repo} on branch {branch}",
                updatedSubscription.Id,
                ex.FilePath,
                ex.RepositoryUri,
                ex.BranchName);
            throw;
        }
    }
    public async Task AddChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml channel)
    {
        try
        {
            await PerformConfigurationRepositoryOperationInternal(
                parameters,
                channel,
                (p, repo, branch, c) => AddModelInternalAsync<ChannelYaml, string>(
                    p, repo, branch, c,
                    ConfigFilePathResolver.GetDefaultChannelFilePath,
                    (existing, newChannel) => string.Equals(existing.Name, newChannel.Name, StringComparison.OrdinalIgnoreCase),
                    new ChannelYamlComparer(),
                    $"Add new channel '{c.Name}'"),
                $"Successfully added channel '{channel.Name}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");
        }
        catch (DuplicateConfigurationObjectException ex)
        {
            _logger.LogError("Channel with name '{name}' already exists in '{filePath}'.",
               channel.Name,
               ex.FilePath);
            throw;
        }
    }

    private async Task PerformConfigurationRepositoryOperationInternal<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        TModel yamlModel,
        Func<ConfigurationRepositoryOperationParameters, IGitRepo, string, TModel, Task> operation,
        string noPrOperationMessage)
        where TModel : IYamlModel
    {
        var configurationRepo = await _configurationRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        await operation(parameters, configurationRepo, workingBranch, yamlModel);

        if (!parameters.DontOpenPr)
        {
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                "Updating Maestro configuration");
        }
        else
        {
            _logger.LogInformation("{message}", noPrOperationMessage);
        }
    }

    /// <summary>
    /// Generic method to add a new configuration object to the configuration repository.
    /// </summary>
    private async Task AddModelInternalAsync<TModel, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel,
        Func<TModel, string> getDefaultFilePath,
        Func<TModel, TModel, bool> isEquivalent,
        IComparer<TModel> modelComparer,
        string commitMessage)
        where TModel : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var filePath = string.IsNullOrEmpty(parameters.ConfigurationFilePath)
            ? getDefaultFilePath(yamlModel)
            : parameters.ConfigurationFilePath;
        _logger.LogInformation("Adding new configuration to file {0}", filePath);

        var yamlModelsInFile = await FetchAndParseRemoteConfiguration<TModel>(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath);

        var equivalentInFile = yamlModelsInFile.FirstOrDefault(m => isEquivalent(m, yamlModel));
        if (equivalentInFile != null)
        {
            throw new DuplicateConfigurationObjectException(filePath);
        }

        yamlModelsInFile.Add(yamlModel);
        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsInFile,
            modelComparer,
            commitMessage);
    }

    /// <summary>
    /// Generic method to delete a configuration object from the configuration repository.
    /// </summary>
    private async Task DeleteModelInternalAsync<TModel, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel,
        Func<TModel, TKey> getModelId,
        IComparer<TModel> modelComparer,
        string commitMessage)
        where TModel : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            yamlModel,
            getModelId);

        var yamlModelKey = getModelId(yamlModel);
        var yamlModelsWithoutDeleted = yamlModelsInFile.Where(m => !getModelId(m).Equals(yamlModelKey)).ToList();

        if (yamlModelsInFile.Count == yamlModelsWithoutDeleted.Count)
        {
            var branchName = parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch;
            throw new ConfigurationObjectNotFoundException(
                filePath,
                parameters.RepositoryUri,
                branchName);
        }

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsWithoutDeleted,
            modelComparer,
            commitMessage);
    }

    /// <summary>
    /// Generic method to update an existing configuration object in the configuration repository.
    /// </summary>
    private async Task UpdateModelInternalAsync<TModel, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel updatedYamlModel,
        Func<TModel, TKey> getModelId,
        IComparer<TModel> modelComparer,
        string commitMessage)
        where TModel : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            updatedYamlModel,
            getModelId);

        var yamlModelKey = getModelId(updatedYamlModel);
        var existingYamlModel = yamlModelsInFile.FirstOrDefault(m => getModelId(m).Equals(yamlModelKey));
        if (existingYamlModel == null)
        {
            var branchName = parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch;
            throw new ConfigurationObjectNotFoundException(
                filePath,
                parameters.RepositoryUri,
                branchName);
        }

        var index = yamlModelsInFile.IndexOf(existingYamlModel);
        yamlModelsInFile[index] = updatedYamlModel;

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsInFile,
            modelComparer,
            commitMessage);
    }

    /// <summary>
    /// Gets the file path and yaml models, either from the specified path or by searching.
    /// </summary>
    private async Task<(string FilePath, List<TModel> YamlModels)> GetFilePathAndModels<TModel, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel,
        Func<TModel, TKey> getModelId)
        where TModel : IYamlModel
        where TKey : IEquatable<TKey>
    {
        if (string.IsNullOrEmpty(parameters.ConfigurationFilePath))
        {
            return await FindAndParseConfigurationFile(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                yamlModel,
                getModelId);
        }
        else
        {
            var yamlModels = await FetchAndParseRemoteConfiguration<TModel>(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationFilePath);
            return (parameters.ConfigurationFilePath, yamlModels);
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

    private static async Task CommitConfigurationDataAsync<TModel>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        string filePath,
        IEnumerable<TModel> data,
        IComparer<TModel> modelComparer,
        string commitMessage)
        where TModel : IYamlModel
    {
        if (!data.Any())
        {
            await gitRepo.DeleteFileAsync(repositoryUri, workingBranch, filePath, commitMessage);
        }
        else
        {
            string yamlContent = _yamlSerializer.Serialize(data.OrderBy(x => x, modelComparer)).Replace("\n-", "\n\n-");
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

    private async Task<(string, List<TModel>)> FindAndParseConfigurationFile<TModel, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject,
        Func<TModel, TKey> getUniqueKey)
        where TModel : IYamlModel
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

    private async Task<(string FilePath, List<TModel> Content)?> TryFindInDefaultFileAsync<TModel, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject,
        Func<TModel, TKey> getUniqueKey)
        where TModel : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var defaultFilePath = ConfigFilePathResolver.GetDefaultFilePath(searchObject);
        var searchKey = getUniqueKey(searchObject);

        try
        {
            var defaultFileContents = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, defaultFilePath);
            var deserializedYamls = _yamlDeserializer.Deserialize<List<TModel>>(defaultFileContents);

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

    private async Task<(string FilePath, List<TModel> Content)> SearchAllFilesInFolderAsync<TModel, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject,
        Func<TModel, TKey> getUniqueKey)
        where TModel : IYamlModel
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
                var deserializedYamls = _yamlDeserializer.Deserialize<List<TModel>>(fileContent);

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
