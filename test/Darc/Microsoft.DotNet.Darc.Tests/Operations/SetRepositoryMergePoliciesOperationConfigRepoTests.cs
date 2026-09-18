// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class SetRepositoryMergePoliciesOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<SetRepositoryMergePoliciesOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<SetRepositoryMergePoliciesOperation>>();
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_WithConfigRepo_CreatesNewFile()
    {
        // Arrange
        const string repository = "https://github.com/dotnet/test-repo";
        const string branch = "main";
        string testBranch = GetTestBranch();
        SetupGetRepositoriesAsync(repository, branch, []);
        var operation = CreateOperation(CreateOptions(repository, branch, true, ["license/cla"], testBranch));

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);
        BranchMergePoliciesYaml actual = await GetWrittenConfigurationAsync(repository, branch, testBranch);
        actual.MergePrs.Should().BeTrue();
        actual.IgnoredChecks.Should().BeEquivalentTo(["license/cla"]);
        actual.MergePolicies.Should().BeEmpty();
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_WithConfigRepo_UpdatesExistingSettings()
    {
        // Arrange
        const string repository = "https://github.com/dotnet/test-repo";
        const string branch = "main";
        string testBranch = GetTestBranch();
        SetupGetRepositoriesAsync(repository, branch,
        [
            new RepositoryBranch(true)
            {
                Repository = repository,
                Branch = branch,
                IgnoredChecks = ["old-check"]
            }
        ]);
        await CreateRepositoryConfigurationAsync(repository, branch);
        var operation = CreateOperation(CreateOptions(repository, branch, true, ["new-check"], testBranch));

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);
        BranchMergePoliciesYaml actual = await GetWrittenConfigurationAsync(repository, branch, testBranch);
        actual.MergePrs.Should().BeTrue();
        actual.IgnoredChecks.Should().BeEquivalentTo(["new-check"]);
        actual.MergePolicies.Should().BeEmpty();
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_DisablingExistingConfiguration_UpdatesFile()
    {
        // Arrange
        const string repository = "https://github.com/dotnet/test-repo";
        const string branch = "main";
        string testBranch = GetTestBranch();
        SetupGetRepositoriesAsync(repository, branch,
        [
            new RepositoryBranch(true)
            {
                Repository = repository,
                Branch = branch,
                IgnoredChecks = ["old-check"]
            }
        ]);
        await CreateRepositoryConfigurationAsync(repository, branch);
        var operation = CreateOperation(CreateOptions(repository, branch, false, ["new-check"], testBranch));

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);
        BranchMergePoliciesYaml actual = await GetWrittenConfigurationAsync(repository, branch, testBranch);
        actual.MergePrs.Should().BeFalse();
        actual.IgnoredChecks.Should().BeEquivalentTo(["new-check"]);
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_DisablingMissingConfiguration_CreatesFile()
    {
        // Arrange
        const string repository = "https://github.com/dotnet/test-repo";
        const string branch = "main";
        string testBranch = GetTestBranch();
        SetupGetRepositoriesAsync(repository, branch, []);
        var operation = CreateOperation(CreateOptions(repository, branch, false, ["license/cla"], testBranch));

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);
        BranchMergePoliciesYaml actual = await GetWrittenConfigurationAsync(repository, branch, testBranch);
        actual.MergePrs.Should().BeFalse();
        actual.IgnoredChecks.Should().BeEquivalentTo(["license/cla"]);
    }

    private void SetupGetRepositoriesAsync(string repository, string branch, IEnumerable<RepositoryBranch> repositoryBranches)
    {
        BarClientMock
            .Setup(client => client.GetRepositoryBranch(repository, branch))
            .ReturnsAsync(repositoryBranches.SingleOrDefault()!);
    }

    private SetRepositoryMergePoliciesCommandLineOptions CreateOptions(
        string repository,
        string branch,
        bool mergePrs,
        IReadOnlyCollection<string> ignoredChecks,
        string configurationBranch) => new()
        {
            Repository = repository,
            Branch = branch,
            MergePrs = mergePrs,
            IgnoreChecks = ignoredChecks,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
            Quiet = true
        };

    private SetRepositoryMergePoliciesOperation CreateOperation(SetRepositoryMergePoliciesCommandLineOptions options) => new(
        options,
        BarClientMock.Object,
        RemoteFactoryMock.Object,
        ConfigurationRepositoryManager,
        _loggerMock.Object);

    private async Task<string> CreateRepositoryConfigurationAsync(string repository, string branch)
    {
        BranchMergePoliciesYaml configuration = new()
        {
            Repository = repository,
            Branch = branch,
            MergePrs = true,
            IgnoredChecks = ["old-check"],
            MergePolicies = [],
        };
        string filePath = ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath(configuration);
        string content = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build()
            .Serialize(new[] { configuration });
        await CreateFileInConfigRepoAsync(filePath, content);
        return filePath;
    }

    private async Task<BranchMergePoliciesYaml> GetWrittenConfigurationAsync(string repository, string branch, string testBranch)
    {
        await CheckoutBranch(testBranch);
        string filePath = ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath(new BranchMergePoliciesYaml
        {
            Repository = repository,
            Branch = branch
        });
        string content = await File.ReadAllTextAsync(Path.Combine(ConfigurationRepoPath, filePath));
        List<BranchMergePoliciesYaml> configurations = YamlDeserializer.Deserialize<List<BranchMergePoliciesYaml>>(content) ?? [];
        configurations.Should().ContainSingle();
        return configurations[0];
    }
}