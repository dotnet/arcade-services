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

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Operation class for configuration management commands.
/// </summary>
internal abstract class ConfigurationManagementOperation : Operation
{
    protected static UnixPath DefaultChannelConfigurationFileName = new("default-channels/default-channels.yml");
    protected static UnixPath ChannelConfigurationFileName = new("channels/channels.yml");
    protected static UnixPath SubscriptionConfigurationFolderPath = new("subscriptions");

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
    private readonly ILocalGitRepoFactory _localGitRepoFactory;

    protected ConfigurationManagementOperation(
        IConfigurationManagementCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        IRemoteFactory remoteFactory,
        ILogger logger,
        ILocalGitRepoFactory localGitRepoFactory)
    {
        _options = options;
        _remoteFactory = remoteFactory;
        _logger = logger;

        _configurationRepo = gitRepoFactory.CreateClient(_options.ConfigurationRepository);
        _localGitRepoFactory = localGitRepoFactory;
    }

    protected async Task CreateConfigurationBranchIfNeeded()
    {
        var color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"⚠️⚠️⚠️ Maestro channels and subscriptions are now managed as code via a configuration repository", _options.ConfigurationRepository);
        Console.ForegroundColor = color;

        if (!string.IsNullOrEmpty(_options.ConfigurationBranch))
        {
            if (!await _configurationRepo.BranchExists(_options.ConfigurationRepository, _options.ConfigurationBranch))
            {
                if (string.IsNullOrEmpty(_options.ConfigurationBaseBranch))
                {
                    throw new ArgumentException("A base branch must be specified when the configuration branch does not exist.");
                }

                Console.WriteLine("The specified configuration branch '{0}' does not exist. Creating it...", _options.ConfigurationBranch);
                await _configurationRepo.CreateNewBranchAsync(_options.ConfigurationRepository, _options.ConfigurationBranch, _options.ConfigurationBaseBranch);
                return;
            }

            Console.WriteLine("Using existing configuration branch {0}", _options.ConfigurationBranch);
            return;
        }

        if (string.IsNullOrEmpty(_options.ConfigurationBaseBranch))
        {
            throw new ArgumentException("A base branch must be specified when the configuration branch is not.");
        }

        if (!await _configurationRepo.BranchExists(_options.ConfigurationRepository, _options.ConfigurationBaseBranch))
        {
            throw new ArgumentException($"The specified base branch '{_options.ConfigurationBaseBranch}' does not exist.");
        }

        _options.ConfigurationBranch = $"darc/{_options.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
        Console.WriteLine("Creating new configuration branch {0}", _options.ConfigurationBranch);
        await _configurationRepo.CreateNewBranchAsync(_options.ConfigurationRepository, _options.ConfigurationBaseBranch, _options.ConfigurationBranch);
    }

    protected async Task<List<T>> GetConfiguration<T>(string fileName, string? branch = null)
    {
        branch ??= _options.ConfigurationBranch;

        try
        {
            var contents = await _configurationRepo.GetFileContentsAsync(
                fileName,
                _options.ConfigurationRepository,
                branch);

            return _yamlDeserializer.Deserialize<List<T>>(contents) ?? [];
        }
        catch (DependencyFileNotFoundException)
        {
            return [];
        }
    }

    protected async Task WriteConfigurationFile(string fileName, object content, string commitMessage)
    {
        _logger.LogInformation("Pushing changes of {fileName} to {branch}...", fileName, _options.ConfigurationBranch);
        
        string yamlContent = _yamlSerializer.Serialize(content);
        
        // Add empty lines between YAML list items (lines starting with "- ")
        var lines = yamlContent.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        var modifiedLines = new List<string>();
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // If this line starts a new list item (starts with "- ") and it's not the first item,
            // add an empty line before it
            if (line.StartsWith("- ") && i > 0 && modifiedLines.Count > 0)
            {
                modifiedLines.Add(string.Empty);
            }
            
            modifiedLines.Add(line);
        }
        
        string formattedYamlContent = string.Join(Environment.NewLine, modifiedLines);
        
        await _configurationRepo.CommitFilesAsync(
            [
                new GitFile(fileName, formattedYamlContent)
            ],
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

    protected async Task RemoveConfigurationFile(string fileName)
    {
        Console.WriteLine("Removing {0} from {1}...", fileName, _options.ConfigurationBranch);
        await _configurationRepo.CommitFilesAsync(
            [
                new GitFile(fileName, null, ContentEncoding.Utf8, operation: GitFileOperation.Delete)
            ],
            _options.ConfigurationRepository,
            _options.ConfigurationBranch,
            "Removing " + fileName);
    }

    protected async Task CreatePullRequest(string repoUri, string headBranch, string targetBranch, string title, string? description = null)
    {
        Console.WriteLine("Creating pull request from {0} to {1}...", headBranch, targetBranch);
        var remote = await _remoteFactory.CreateRemoteAsync(repoUri);
        var prUrl = await remote.CreatePullRequestAsync(repoUri, new PullRequest()
        {
            HeadBranch = headBranch,
            BaseBranch = targetBranch,
            Title = title,
            Description = description,
        });
        var prId = prUrl.Substring(prUrl.LastIndexOf('/') + 1);
        prUrl = $"{repoUri}/pullrequest/{prId}";
        Console.WriteLine("Created pull request at {0}", prUrl);
    }
}
