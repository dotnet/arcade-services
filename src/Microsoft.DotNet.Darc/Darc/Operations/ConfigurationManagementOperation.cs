// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Operation class for configuration management commands.
/// </summary>
internal abstract class ConfigurationManagementOperation : Operation
{
    protected const string ChannelConfigurationFileName = "channels/channels.yaml";

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    private readonly IConfigurationManagementCommandLineOptions _options;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ILogger _logger;
    private readonly IGitRepo _configurationRepo;

    protected ConfigurationManagementOperation(
        IConfigurationManagementCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        IRemoteFactory remoteFactory,
        ILogger logger)
    {
        _options = options;
        _remoteFactory = remoteFactory;
        _logger = logger;

        _configurationRepo = gitRepoFactory.CreateClient(_options.ConfigurationRepository);
    }

    protected async Task CreateConfigurationBranchIfNeeded()
    {
        var remote = await _remoteFactory.CreateRemoteAsync(_options.ConfigurationRepository);

        if (!string.IsNullOrEmpty(_options.ConfigurationBranch))
        {
            if (!await remote.BranchExistsAsync(_options.ConfigurationRepository, _options.ConfigurationBranch))
            {
                if (string.IsNullOrEmpty(_options.ConfigurationBaseBranch))
                {
                    throw new ArgumentException("A base branch must be specified when the configuration branch does not exist.");
                }

                _logger.LogInformation("The specified configuration branch '{branch}' does not exist. Creating it...", _options.ConfigurationBranch);
                await remote.CreateNewBranchAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch, _options.ConfigurationBranch);
            }

            _logger.LogInformation("Using existing configuration branch {branch}", _options.ConfigurationBranch);
            return;
        }

        if (string.IsNullOrEmpty(_options.ConfigurationBaseBranch))
        {
            throw new ArgumentException("A base branch must be specified when the configuration branch is not.");
        }

        if (!await remote.BranchExistsAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch))
        {
            throw new ArgumentException($"The specified base branch '{_options.ConfigurationBaseBranch}' does not exist.");
        }

        _options.ConfigurationBranch = $"darc/{_options.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
        _logger.LogInformation("Creating new configuration branch {branch}", _options.ConfigurationBranch);
        await remote.CreateNewBranchAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch, _options.ConfigurationBranch);
    }

    protected async Task<List<T>> GetConfiguration<T>(string fileName)
    {
        _logger.LogInformation("Reading configuration from {fileName}...", fileName);
        var contents = await _configurationRepo.GetFileContentsAsync(
            fileName,
            _options.ConfigurationRepository,
            _options.ConfigurationBranch);

        return _yamlDeserializer.Deserialize<List<T>>(contents);
    }

    protected async Task WriteConfigurationFile(string fileName, object content, string commitMessage)
    {
        _logger.LogInformation("Pushing changes of {fileName} to {branch}...", fileName, _options.ConfigurationBranch);
        await _configurationRepo.CommitFilesAsync(
            [
                new GitFile(fileName, _yamlSerializer.Serialize(content))
            ],
            _options.ConfigurationRepository,
            _options.ConfigurationBranch,
            commitMessage);
    }

    protected async Task RemoveConfigurationFile(string fileName)
    {
        _logger.LogInformation("Removing {fileName} from {branch}...", fileName, _options.ConfigurationBranch);
        await _configurationRepo.CommitFilesAsync(
            [
                new GitFile(fileName, null, ContentEncoding.Utf8, operation: GitFileOperation.Delete)
            ],
            _options.ConfigurationRepository,
            _options.ConfigurationBranch,
            "Removing " + fileName);
    }

    protected async Task CreatePullRequest(string repoUri, string headBranch, string targetBranch, string title, string description = null)
    {
        _logger.LogInformation("Creating pull request from {headBranch} to {targetBranch}...", headBranch, targetBranch);
        var remote = await _remoteFactory.CreateRemoteAsync(repoUri);
        var prUrl = await remote.CreatePullRequestAsync(repoUri, new PullRequest()
        {
            HeadBranch = headBranch,
            BaseBranch = targetBranch,
            Title = title,
            Description = description,
        });
        _logger.LogInformation("Created pull request at {prUrl}", prUrl);
    }
}
