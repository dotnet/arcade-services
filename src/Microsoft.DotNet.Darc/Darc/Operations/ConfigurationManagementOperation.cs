// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal abstract class ConfigurationManagementOperation : Operation
{
    private const string UseConfigRepositoryEnvVar = "DARC_USE_CONFIGURATION_REPOSITORY";

    private readonly IConfigurationManagementCommandLineOptions _options;
    protected readonly IRemoteFactory _remoteFactory;
    protected readonly ILogger _logger;
    protected readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IGitRepo _configurationRepo;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    protected ConfigurationManagementOperation(
        IConfigurationManagementCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        ILocalGitRepoFactory localGitRepoFactory,
        IRemoteFactory remoteFactory,
        ILogger logger)
    {
        _options = options;
        _remoteFactory = remoteFactory;
        _logger = logger;
        _gitRepoFactory = gitRepoFactory;
        _localGitRepoFactory = localGitRepoFactory;

        _configurationRepo = gitRepoFactory.CreateClient(_options.ConfigurationRepository);
    }

    protected static bool ShouldUseConfigurationRepository()
    {
        var val = Environment.GetEnvironmentVariable(UseConfigRepositoryEnvVar);
        return !string.IsNullOrEmpty(val) && bool.Parse(val);
    }

    // Validates that the configuration repository parameters are correct
    protected async Task ValidateConfigurationRepositoryParametersAsync()
    {
        if (!await _configurationRepo.RepoExistsAsync(_options.ConfigurationRepository))
        {
            throw new ArgumentException($"The configuration repository '{_options.ConfigurationRepository}' is not a valid git repository.");
        }

        bool configurationBranchProvided = !string.IsNullOrEmpty(_options.ConfigurationBranch);
        bool configurationBaseBranchProvided = !string.IsNullOrEmpty(_options.ConfigurationBaseBranch);
        if (configurationBranchProvided == configurationBaseBranchProvided)
        {
            throw new ArgumentException($"Exactly one of configuration branch and configuration base branch must be specified");
        }

        if (configurationBranchProvided)
        {
            if (!await _configurationRepo.DoesBranchExistAsync(_options.ConfigurationRepository, _options.ConfigurationBranch))
            {
                throw new ArgumentException($"The configuration branch '{_options.ConfigurationBranch}' does not exist in the repository '{_options.ConfigurationRepository}'.");
            }
        }

        if (configurationBaseBranchProvided)
        {
            if (!await _configurationRepo.DoesBranchExistAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch))
            {
                throw new ArgumentException($"The configuration base branch '{_options.ConfigurationBaseBranch}' does not exist in the repository '{_options.ConfigurationRepository}'.");
            }
        }

        if (!string.IsNullOrEmpty(_options.ConfigurationFileName))
        {
            if (_options.ConfigurationFileName != Path.GetFileName(_options.ConfigurationFileName))
            {
                throw new ArgumentException($"The configuration file name '{_options.ConfigurationFileName}' must be a file name only, not a path.");
            }

            if (!_options.ConfigurationFileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(_options.ConfigurationFileName);
                if (!string.IsNullOrEmpty(extension))
                {
                    _logger.LogWarning("Replacing file extension '{Extension}' with '.yml'", extension);
                    _options.ConfigurationFileName = Path.ChangeExtension(_options.ConfigurationFileName, ".yml");
                }
                else
                {
                    _options.ConfigurationFileName += ".yml";
                }
            }
        }
    }

    // Gets the working configuration branch, creating it if necessary
    protected async Task SetWorkingConfigurationBranchAsync()
    {
        if (string.IsNullOrEmpty(_options.ConfigurationBranch))
        {
            _options.ConfigurationBranch = await CreateConfigurationBranchAsync();
        }
    }

    protected async Task<string> CreateConfigurationBranchAsync()
    {
        var branch = $"darc/{_options.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
        await _configurationRepo.CreateBranchAsync(
            _options.ConfigurationRepository,
            branch,
            _options.ConfigurationBaseBranch);
        return branch;
    }

    protected async Task<List<TData>> FetchAndParseRemoteConfiguration<TData>(string filePath)
    {
        string fileContents;
        try
        {
            fileContents = await _configurationRepo.GetFileContentsAsync(
                filePath,
                _options.ConfigurationRepository,
                _options.ConfigurationBranch);
            return _yamlDeserializer.Deserialize<List<TData>>(fileContents);
        }
        catch (DependencyFileNotFoundException)
        {
            return [];
        }
    }

    protected async Task WriteConfigurationDataAsync(string filePath, IEnumerable<object> data, string commitMessage)
    {
        string yamlContents = _yamlSerializer.Serialize(data).Replace("\n-", "\n\n-");
        GitFile fileToCommit = new(filePath, yamlContents);
        await _configurationRepo.CommitFilesAsync(
            [fileToCommit],
            _options.ConfigurationRepository,
            _options.ConfigurationBranch,
            commitMessage);

        if (_configurationRepo.GetType() == typeof(LocalLibGit2Client))
        {
            var local = _localGitRepoFactory.Create(new NativePath(_options.ConfigurationRepository));
            await local.StageAsync(["."]);
            await local.CommitAsync(commitMessage, allowEmpty: false);
        }
    }

    protected async Task CreatePullRequest(string title, string? description = null)
    {
        if (_configurationRepo.GetType() == typeof(LocalLibGit2Client))
        {
            throw new InvalidOperationException("Cannot create pull request when using local git repository. Specify a remote repository as the configuration repository");
        }
        Console.WriteLine("Creating pull request from {0} to {1}...", _options.ConfigurationBranch, _options.ConfigurationBaseBranch);
        var remote = await _remoteFactory.CreateRemoteAsync(_options.ConfigurationRepository);
        var pr = await remote.CreatePullRequestAsync(_options.ConfigurationRepository, new PullRequest()
        {
            HeadBranch = _options.ConfigurationBranch,
            BaseBranch = _options.ConfigurationBaseBranch,
            Title = title,
            Description = description,
        });
        var prId = pr.Url.Substring(pr.Url.LastIndexOf('/') + 1);
        var guiUri = $"{_options.ConfigurationRepository}/pullrequest/{prId}";
        Console.WriteLine("Created pull request at {0}", guiUri);
    }
}
