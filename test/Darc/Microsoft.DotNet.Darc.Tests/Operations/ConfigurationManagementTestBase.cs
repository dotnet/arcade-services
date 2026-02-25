// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Maestro.Common.AzureDevOpsTokens;
using Moq;
using NUnit.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.DotNet.MaestroConfiguration.Client;
using GitRepoFactory = Microsoft.DotNet.DarcLib.GitRepoFactory;
using IGitRepoFactory = Microsoft.DotNet.DarcLib.IGitRepoFactory;
using Maestro.Common;
using Maestro.Common.Telemetry;

namespace Microsoft.DotNet.Darc.Tests.Operations;

/// <summary>
/// Base class for testing operations that use the configuration repository.
/// Creates a real local git repository for testing, which gets cleaned up afterwards.
/// </summary>
public abstract class ConfigurationManagementTestBase
{
    private const string UseConfigRepositoryEnvVar = "DARC_USE_CONFIGURATION_REPOSITORY";

    protected ConsoleOutputIntercepter ConsoleOutput = null!;
    protected Mock<IBarApiClient> BarClientMock = null!;
    protected Mock<IRemoteFactory> RemoteFactoryMock = null!;
    protected Mock<IRemote> RemoteMock = null!;

    // Real implementations for local git operations
    protected LocalLibGit2Client GitClient = null!;
    protected IProcessManager ProcessManager = null!;
    protected IGitRepoFactory GitRepoFactory = null!;
    protected ILocalGitRepoFactory LocalGitRepoFactory = null!;
    protected MaestroConfiguration.Client.IGitRepoFactory ConfigurationRepositoryGitRepoFactory = null!;
    protected IConfigurationRepositoryManager ConfigurationRepositoryManager = null!;
    

    /// <summary>
    /// Path to the temporary configuration repository.
    /// </summary>
    protected string ConfigurationRepoPath { get; private set; } = null!;

    /// <summary>
    /// The default branch name in the configuration repository.
    /// </summary>
    protected const string DefaultBranch = "production";

    /// <summary>
    /// YAML deserializer for reading configuration files.
    /// </summary>
    protected static IDeserializer YamlDeserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [SetUp]
    public virtual async Task SetupAsync()
    {
        // Enable configuration repository mode
        Environment.SetEnvironmentVariable(UseConfigRepositoryEnvVar, "true");

        ConsoleOutput = new ConsoleOutputIntercepter();

        await SetupLocalGitRepoAsync();
        SetupBarClientMock();
        SetupRemoteMocks();
        SetupRepoFactories();

        ConfigurationRepositoryGitRepoFactory = new DarcLib.ConfigurationRepository.GitRepoFactory(
            GitRepoFactory,
            LocalGitRepoFactory,
            RemoteFactoryMock.Object,
            NullLoggerFactory.Instance);
        ConfigurationRepositoryManager = new ConfigurationRepositoryManager(
            ConfigurationRepositoryGitRepoFactory,
            NullLogger<IConfigurationRepositoryManager>.Instance);
    }

    [TearDown]
    public virtual void TearDown()
    {
        // Disable configuration repository mode
        Environment.SetEnvironmentVariable(UseConfigRepositoryEnvVar, null);

        ConsoleOutput.Dispose();
        CleanupTempRepo();
    }

    /// <summary>
    /// Creates a real local git repository for testing.
    /// </summary>
    private async Task SetupLocalGitRepoAsync()
    {
        ConfigurationRepoPath = Path.Combine(Path.GetTempPath(), $"darc-test-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ConfigurationRepoPath);

        ProcessManager = new ProcessManager(NullLogger.Instance, "git");
        GitClient = new LocalLibGit2Client(
            new RemoteTokenProvider(),
            new NoTelemetryRecorder(),
            ProcessManager,
            new FileSystem(),
            NullLogger.Instance);

        // Initialize git repo
        await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["init", "-b", DefaultBranch]);
        await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["config", "user.email", DarcLib.Constants.DarcBotEmail]);
        await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["config", "user.name", DarcLib.Constants.DarcBotName]);

        // Create initial commit so we have a valid branch
        var readmePath = Path.Combine(ConfigurationRepoPath, "README.md");
        await File.WriteAllTextAsync(readmePath, "# Test Configuration Repository\n");
        await GitClient.StageAsync(ConfigurationRepoPath, ["."]);
        await GitClient.CommitAsync(ConfigurationRepoPath, "Initial commit", allowEmpty: false, author: null);
    }

    private void SetupRepoFactories()
    {
        GitRepoFactory = new GitRepoFactory(
            new RemoteTokenProvider(),
            Mock.Of<IAzureDevOpsTokenProvider>(),
            new NoTelemetryRecorder(),
            ProcessManager,
            new FileSystem(),
            NullLoggerFactory.Instance,
            temporaryPath: null!);

        var localGitClient = new LocalGitClient(
            new RemoteTokenProvider(),
            new NoTelemetryRecorder(),
            ProcessManager,
            new FileSystem(),
            NullLogger.Instance);
        LocalGitRepoFactory = new LocalGitRepoFactory(localGitClient, ProcessManager);
    }

    protected virtual void SetupBarClientMock()
    {
        BarClientMock = new Mock<IBarApiClient>();

        BarClientMock
            .Setup(x => x.GetSubscriptionsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync([]);
    }

    protected virtual void SetupRemoteMocks()
    {
        RemoteMock = new Mock<IRemote>();
        RemoteFactoryMock = new Mock<IRemoteFactory>();

        RemoteFactoryMock
            .Setup(x => x.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(RemoteMock.Object);

        RemoteMock
            .Setup(x => x.RepositoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        RemoteMock
            .Setup(x => x.GetLatestCommitAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("abc123");

        RemoteMock
            .Setup(x => x.GetDependenciesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<UnixPath?>()))
            .ReturnsAsync(Array.Empty<DependencyDetail>());
    }

    protected string GetTestBranch() => $"test-branch-{Guid.NewGuid()}";

    protected void SetupChannel(string channelName, int channelId = 1)
    {
        BarClientMock
            .Setup(x => x.GetChannelAsync(channelName))
            .ReturnsAsync(new ProductConstructionService.Client.Models.Channel(channelId, channelName, "test"));
    }

    /// <summary>
    /// Creates an existing file in the configuration repository.
    /// </summary>
    protected async Task CreateFileInConfigRepoAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(ConfigurationRepoPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content);
        await GitClient.StageAsync(ConfigurationRepoPath, [relativePath]);
        await GitClient.CommitAsync(ConfigurationRepoPath, $"Add {relativePath}", allowEmpty: false, author: null);
    }

    protected async Task<string> GetCurrentBranchAsync()
    {
        var result = await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["rev-parse", "--abbrev-ref", "HEAD"]);
        return result.StandardOutput.Trim();
    }

    protected async Task<List<string>> GetBranchesAsync()
    {
        var result = await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["branch", "--list", "--format=%(refname:short)"]);
        var branches = new List<string>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            branches.Add(line.Trim());
        }
        return branches;
    }

    protected async Task CheckoutBranch(string branch)
    {
        await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["checkout", branch]);
    }

    /// <summary>
    /// Deserializes a YAML file containing a list of subscriptions.
    /// </summary>
    protected static async Task<List<SubscriptionYaml>> DeserializeSubscriptionsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<SubscriptionYaml>>(content) ?? [];
    }

    private void CleanupTempRepo()
    {
        if (string.IsNullOrEmpty(ConfigurationRepoPath))
        {
            return;
        }

        try
        {
            // Need to remove read-only attributes from .git folder files
            var directory = new DirectoryInfo(ConfigurationRepoPath);
            foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }
            Directory.Delete(ConfigurationRepoPath, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
