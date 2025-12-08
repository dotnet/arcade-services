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
using Microsoft.DotNet.DarcLib.Models.Yaml;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

    /// <summary>
    /// Path to the temporary configuration repository.
    /// </summary>
    protected string ConfigurationRepoPath { get; private set; } = null!;

    /// <summary>
    /// The default branch name in the configuration repository.
    /// </summary>
    protected const string DefaultBranch = "main";

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
        SetupGitRepoFactory();
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
        // Create temp directory
        ConfigurationRepoPath = Path.Combine(Path.GetTempPath(), $"darc-test-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ConfigurationRepoPath);

        // Set up git client and process manager
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

    /// <summary>
    /// Sets up the git repo factory to return real LocalLibGit2Client for local paths.
    /// </summary>
    private void SetupGitRepoFactory()
    {
        // Create a factory that returns real LocalLibGit2Client for local paths
        GitRepoFactory = new TestGitRepoFactory(GitClient);
        LocalGitRepoFactory = new LocalGitRepoFactory(
            new LocalGitClient(
                new RemoteTokenProvider(),
                new NoTelemetryRecorder(),
                ProcessManager,
                new FileSystem(),
                NullLogger.Instance),
            ProcessManager);
    }

    /// <summary>
    /// Sets up the BAR API client mock with default behavior.
    /// Override to customize for specific tests.
    /// </summary>
    protected virtual void SetupBarClientMock()
    {
        BarClientMock = new Mock<IBarApiClient>();

        // Default: no existing subscriptions
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

    /// <summary>
    /// Sets up the remote factory mocks for repository verification (source/target repos).
    /// These are mocked because we're only testing local configuration repo operations.
    /// </summary>
    protected virtual void SetupRemoteMocks()
    {
        RemoteMock = new Mock<IRemote>();
        RemoteFactoryMock = new Mock<IRemoteFactory>();

        // Setup factory to return our mock
        RemoteFactoryMock
            .Setup(x => x.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(RemoteMock.Object);

        // Default: repository exists
        RemoteMock
            .Setup(x => x.RepositoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Default: branch exists (GetLatestCommitAsync is used for source-enabled verification)
        RemoteMock
            .Setup(x => x.GetLatestCommitAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("abc123");

        // Default: dependencies exist (GetDependenciesAsync is used for branch verification)
        RemoteMock
            .Setup(x => x.GetDependenciesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<UnixPath?>()))
            .ReturnsAsync(Array.Empty<DependencyDetail>());
    }

    /// <summary>
    /// Configures the mock to simulate that a channel exists with the given name.
    /// </summary>
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

    /// <summary>
    /// Reads file content from the configuration repository.
    /// </summary>
    protected async Task<string?> ReadFileFromConfigRepoAsync(string relativePath, string? branch = null)
    {
        branch ??= DefaultBranch;

        // Switch to the branch if needed
        var currentBranch = await GetCurrentBranchAsync();
        if (currentBranch != branch)
        {
            await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["checkout", branch]);
        }

        var fullPath = Path.Combine(ConfigurationRepoPath, relativePath);
        if (!File.Exists(fullPath))
        {
            // Switch back if we changed
            if (currentBranch != branch)
            {
                await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["checkout", currentBranch]);
            }
            return null;
        }

        var content = await File.ReadAllTextAsync(fullPath);

        // Switch back if we changed
        if (currentBranch != branch)
        {
            await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["checkout", currentBranch]);
        }

        return content;
    }

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    protected async Task<string> GetCurrentBranchAsync()
    {
        var result = await ProcessManager.ExecuteGit(ConfigurationRepoPath, ["rev-parse", "--abbrev-ref", "HEAD"]);
        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Gets list of branches in the configuration repository.
    /// </summary>
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

    /// <summary>
    /// Checks if a branch exists in the configuration repository.
    /// </summary>
    protected async Task<bool> BranchExistsAsync(string branchName)
    {
        var branches = await GetBranchesAsync();
        return branches.Contains(branchName);
    }

    /// <summary>
    /// Cleans up the temporary repository.
    /// </summary>
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

    /// <summary>
    /// A git repo factory that returns LocalLibGit2Client for local paths.
    /// </summary>
    private class TestGitRepoFactory : IGitRepoFactory
    {
        private readonly LocalLibGit2Client _gitClient;

        public TestGitRepoFactory(LocalLibGit2Client gitClient)
        {
            _gitClient = gitClient;
        }

        public IGitRepo CreateClient(string repoUri)
        {
            // For local paths, return the real LocalLibGit2Client
            if (Directory.Exists(repoUri) || repoUri.StartsWith("/") || (repoUri.Length > 1 && repoUri[1] == ':'))
            {
                return _gitClient;
            }

            throw new ArgumentException($"TestGitRepoFactory only supports local paths, got: {repoUri}");
        }
    }
}
