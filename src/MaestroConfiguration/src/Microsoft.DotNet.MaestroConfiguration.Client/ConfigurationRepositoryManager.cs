// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public class ConfigurationRepositoryManager : IConfigurationRepositoryManager
{
    private enum OperationType
    {
        Add,
        Update,
        Delete
    }

    private readonly IGitRepoFactory _gitRepoFactory;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public ConfigurationRepositoryManager(
        IGitRepoFactory gitRepoFactory)
    {
        _gitRepoFactory = gitRepoFactory;
    }

    public async Task AddSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
    {
        await PerformConfigurationRepositoryOperationInternal(
            parameters,
            subscription,
            OperationType.Add);
    }

    public async Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
    {
        await PerformConfigurationRepositoryOperationInternal(
            parameters,
            subscription,
            OperationType.Delete);
    }

    public async Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription)
    {
        await PerformConfigurationRepositoryOperationInternal(
            parameters,
            updatedSubscription,
            OperationType.Update);
    }

    public async Task AddChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml channel)
    {
        await PerformConfigurationRepositoryOperationInternal(
            parameters,
            channel,
            OperationType.Add);
    }

    private async Task PerformConfigurationRepositoryOperationInternal<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        TModel yamlModel,
        OperationType operationType)
        where TModel : IYamlModel
    {
        var configurationRepo = await _gitRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        switch (operationType)
        {
            case OperationType.Add:
                await AddModelInternalAsync(parameters, configurationRepo, workingBranch, yamlModel);
                break;
            case OperationType.Update:
                await UpdateModelInternalAsync(parameters, configurationRepo, workingBranch, yamlModel);
                break;
            case OperationType.Delete:
                await DeleteModelInternalAsync(parameters, configurationRepo, workingBranch, yamlModel);
                break;
        }

        if (!parameters.DontOpenPr)
        {
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch);
        }
    }

    private async Task AddModelInternalAsync<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel)
        where TModel : IYamlModel
    {
        var filePath = string.IsNullOrEmpty(parameters.ConfigurationFilePath)
            ? yamlModel.GetDefaultFilePath()
            : parameters.ConfigurationFilePath;

        var yamlModelsInFile = await FetchAndParseRemoteConfiguration<TModel>(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath);

        var equivalentInFile = yamlModelsInFile.FirstOrDefault(m => m.GetUniqueId() == yamlModel.GetUniqueId());

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
            yamlModelsInFile);
    }

    private async Task DeleteModelInternalAsync<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel)
        where TModel : IYamlModel
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            yamlModel);

        var yamlModelsWithoutDeleted = yamlModelsInFile.Where(m => m.GetUniqueId() != yamlModel.GetUniqueId()).ToList();

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
            yamlModelsWithoutDeleted);
    }

    private async Task UpdateModelInternalAsync<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel updatedYamlModel)
        where TModel : IYamlModel
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            updatedYamlModel);

        var existingYamlModel = yamlModelsInFile.FirstOrDefault(m => m.GetUniqueId() == updatedYamlModel.GetUniqueId());
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
            yamlModelsInFile);
    }

    /// <summary>
    /// Gets the file path and yaml models, either from the specified path or by searching.
    /// </summary>
    private async Task<(string FilePath, List<TModel> YamlModels)> GetFilePathAndModels<TModel>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        TModel yamlModel)
        where TModel : IYamlModel
    {
        if (string.IsNullOrEmpty(parameters.ConfigurationFilePath))
        {
            return await FindAndParseConfigurationFile(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                yamlModel);
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
        IEnumerable<TModel> data)
        where TModel : IYamlModel
    {
        var commitMessage = $"Updating configuration in {filePath}";
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
        string targetBranch)
    {
        ArgumentException.ThrowIfNullOrEmpty(headBranch);
        ArgumentException.ThrowIfNullOrEmpty(targetBranch);

        var prUrl = await gitRepo.CreatePullRequestAsync(
            repositoryUri,
            headBranch,
            targetBranch,
            "Updating Maestro Configuration");

        var prId = prUrl.Substring(prUrl.LastIndexOf('/') + 1);
        var guiUri = $"{repositoryUri}/pullrequest/{prId}";
    }

    private async Task<(string, List<TModel>)> FindAndParseConfigurationFile<TModel>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject)
        where TModel : IYamlModel
    {
        return await TryFindInDefaultFileAsync(gitRepo, repositoryUri, workingBranch, searchObject)
            ?? await SearchAllFilesInFolderAsync(gitRepo, repositoryUri, workingBranch, searchObject);
    }

    private async Task<(string FilePath, List<TModel> Content)?> TryFindInDefaultFileAsync<TModel>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject)
        where TModel : IYamlModel
    {
        var defaultFilePath = ConfigFilePathResolver.GetDefaultFilePath(searchObject);

        string? defaultFileContents;
        try
        {
            defaultFileContents = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, defaultFilePath);
        }
        catch (FileNotFoundInRepoException)
        {
            return null;
        }

        var deserializedYamls = _yamlDeserializer.Deserialize<List<TModel>>(defaultFileContents);

        if (deserializedYamls.Any(y => searchObject.GetUniqueId() == y.GetUniqueId()))
        {
            return (defaultFilePath, deserializedYamls);
        }

        return null;
    }

    private async Task<(string FilePath, List<TModel> Content)> SearchAllFilesInFolderAsync<TModel>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        TModel searchObject)
        where TModel : IYamlModel
    {
        var folderPath = ConfigFilePathResolver.GetDefaultFileFolder(searchObject);

        var filePaths = await gitRepo.ListBlobsAsync(repositoryUri, workingBranch, folderPath);

        // Search each file one by one for the object
        foreach (var filePath in filePaths)
        {
            try
            {
                var fileContent = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, filePath);
                var deserializedYamls = _yamlDeserializer.Deserialize<List<TModel>>(fileContent);

                if (deserializedYamls.Any(y => searchObject.GetUniqueId() == y.GetUniqueId()))
                {
                    return (filePath, deserializedYamls);
                }
            }
            catch (FileNotFoundInRepoException)
            {
                // File was listed but couldn't be read, skip it
                continue;
            }
        }

        throw new ConfigurationObjectNotFoundException("", "", "");
    }
    #endregion
}
