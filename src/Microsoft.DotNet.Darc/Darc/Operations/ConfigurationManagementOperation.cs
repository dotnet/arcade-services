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
    private readonly IConfigurationManagementCommandLineOptions _options;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly Lazy<IGitRepo> _configurationRepo;
    protected readonly IRemoteFactory _remoteFactory;
    protected readonly ILogger _logger;
    protected readonly IGitRepoFactory _gitRepoFactory;

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

        _configurationRepo = new Lazy<IGitRepo>(() => gitRepoFactory.CreateClient(_options.ConfigurationRepository));
    }

    // Validates that the configuration repository parameters are correct
    protected async Task ValidateConfigurationRepositoryParametersAsync()
    {
        if (!await _configurationRepo.Value.RepoExistsAsync(_options.ConfigurationRepository))
        {
            throw new ArgumentException($"The configuration repository '{_options.ConfigurationRepository}' is not a valid git repository.");
        }

        if (!await _configurationRepo.Value.DoesBranchExistAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch))
        {
            throw new ArgumentException($"The configuration base branch '{_options.ConfigurationBaseBranch}' does not exist in the repository '{_options.ConfigurationRepository}'.");
        }
    }

    /// <summary>
    /// Ensures that a configuration working branch exists, creating one if necessary.
    /// </summary>
    protected async Task EnsureConfigurationWorkingBranchAsync()
    {
        if (string.IsNullOrEmpty(_options.ConfigurationBranch))
        {
            var branch = $"darc/{_options.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            await _configurationRepo.Value.CreateBranchAsync(
                _options.ConfigurationRepository,
                branch,
                _options.ConfigurationBaseBranch);
            _options.ConfigurationBranch = branch;
        }
        else
        {
            if (!await _configurationRepo.Value.DoesBranchExistAsync(_options.ConfigurationRepository, _options.ConfigurationBranch))
            {
                await _configurationRepo.Value.CreateBranchAsync(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch);
            }
        }
    }

    protected async Task<List<TData>> FetchAndParseRemoteConfiguration<TData>(string filePath)
    {
        string fileContents;
        try
        {
            fileContents = await _configurationRepo.Value.GetFileContentsAsync(
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

    protected async Task WriteConfigurationDataAsync(
        string filePath,
        IEnumerable<object> data,
        string commitMessage)
    {
        string yamlContents = _yamlSerializer.Serialize(data).Replace("\n-", "\n\n-");

        if (_configurationRepo.Value is LocalLibGit2Client)
        {
            GitFile fileToCommit = new(new NativePath(_options.ConfigurationRepository) / filePath, yamlContents);
            await _configurationRepo.Value.CommitFilesAsync(
                [fileToCommit],
                _options.ConfigurationRepository,
                _options.ConfigurationBranch,
                commitMessage);
            var local = _localGitRepoFactory.Create(new NativePath(_options.ConfigurationRepository));
            await local.StageAsync(["."]);
            await local.CommitAsync(commitMessage, allowEmpty: false);
        }
        else
        {
            GitFile fileToCommit = new(filePath, yamlContents);
            var remote = await _remoteFactory.CreateRemoteAsync(_options.ConfigurationRepository);
            await remote.CommitUpdatesWithNoCloningAsync(
                [fileToCommit],
                _options.ConfigurationRepository,
                _options.ConfigurationBranch,
                commitMessage);
        }
    }

    protected async Task CreatePullRequest(
        string title,
        string? description = null)
    {
        if (_configurationRepo.Value is LocalLibGit2Client)
        {
            throw new InvalidOperationException("Cannot create pull request when using local git repository. Specify a remote repository as the configuration repository");
        }
        if (string.IsNullOrEmpty(_options.ConfigurationBaseBranch))
        {
            throw new InvalidOperationException("Cannot create pull request without a configuration base branch specified");
        }

        Console.WriteLine("Creating pull request from {0} to {1}...", _options.ConfigurationBranch, _options.ConfigurationBaseBranch);
        var remote = await _remoteFactory.CreateRemoteAsync(_options.ConfigurationRepository);
        var pr = await remote.CreatePullRequestAsync(_options.ConfigurationRepository, new PullRequest()
        {
            HeadBranch = _options.ConfigurationBranch,
            BaseBranch = _options.ConfigurationBaseBranch,
            Title = title,
            Description = description ?? string.Empty,
        });
        var prId = pr.Url.Substring(pr.Url.LastIndexOf('/') + 1);
        var guiUri = $"{_options.ConfigurationRepository}/pullrequest/{prId}";
        Console.WriteLine("Created pull request at {0}", guiUri);
    }
}
