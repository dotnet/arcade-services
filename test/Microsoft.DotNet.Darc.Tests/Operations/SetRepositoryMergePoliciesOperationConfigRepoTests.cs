// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

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
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var testBranch = GetTestBranch();

        // Mock that no policies exist yet
        SetupGetRepositoryMergePoliciesAsync(repository, branch, null);

        var mergePolicies = new List<MergePolicy>
        {
            new MergePolicy
            {
                Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                Properties = new Dictionary<string, JToken>
                {
                    [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(new[] { "license/cla" })
                }
            }
        };

        var options = CreateSetRepositoryMergePoliciesOptions(repository, branch, mergePolicies, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        await CheckoutBranch(testBranch);
        var expectedFilePath = ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath(new BranchMergePoliciesYaml
        {
            Repository = repository,
            Branch = branch
        });
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath);
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify branch merge policies
        var branchPolicies = await DeserializeBranchMergePoliciesAsync(fullExpectedPath);
        branchPolicies.Should().HaveCount(1);

        var actualPolicy = branchPolicies[0];
        actualPolicy.Repository.Should().Be(repository);
        actualPolicy.Branch.Should().Be(branch);
        actualPolicy.MergePolicies.Should().HaveCount(1);
        actualPolicy.MergePolicies[0].Name.Should().Be(MergePolicyConstants.AllCheckSuccessfulMergePolicyName);
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_WithConfigRepo_AppendsToExistingFile()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch1 = "main";
        var branch2 = "release/8.0";
        var testBranch = GetTestBranch();

        // Mock that no policies exist for branch2 (we're adding new policies)
        SetupGetRepositoryMergePoliciesAsync(repository, branch2, null);

        var configFilePath = ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath(new BranchMergePoliciesYaml
        {
            Repository = repository,
            Branch = branch1
        });

        // Create existing branch policies file
        var existingContent = $$"""
            - Branch: {{branch1}}
              Repository URL: {{repository}}
              Merge Policies:
                - Name: Standard
                  Properties: {}
            """;
        await CreateFileInConfigRepoAsync(configFilePath, existingContent);

        var mergePolicies = new List<MergePolicy>
        {
            new MergePolicy
            {
                Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                Properties = new Dictionary<string, JToken>()
            }
        };

        var options = CreateSetRepositoryMergePoliciesOptions(repository, branch2, mergePolicies, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify both branch policies are present
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath);
        var branchPolicies = await DeserializeBranchMergePoliciesAsync(fullPath);
        branchPolicies.Should().HaveCount(2);

        branchPolicies.Should().Contain(p => p.Branch == branch1);
        branchPolicies.Should().Contain(p => p.Branch == branch2);
    }

    [Test]
    public async Task SetRepositoryMergePoliciesOperation_WithConfigRepo_UpdatesExistingPolicies()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var testBranch = GetTestBranch();

        // Mock that policies already exist (so we update instead of add)
        var existingPolicies = new List<MergePolicy>
        {
            new MergePolicy
            {
                Name = "Standard",
                Properties = new Dictionary<string, JToken>()
            }
        };
        SetupGetRepositoryMergePoliciesAsync(repository, branch, existingPolicies);

        var configFilePath = ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath(new BranchMergePoliciesYaml
        {
            Repository = repository,
            Branch = branch
        });

        // Create existing branch policies file
        var existingContent = $$"""
            - Branch: {{branch}}
              Repository URL: {{repository}}
              Merge Policies:
                - Name: Standard
                  Properties: {}
            """;
        await CreateFileInConfigRepoAsync(configFilePath, existingContent);

        var updatedMergePolicies = new List<MergePolicy>
        {
            new MergePolicy
            {
                Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                Properties = new Dictionary<string, JToken>
                {
                    [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(new[] { "WIP", "license/cla" })
                }
            },
            new MergePolicy
            {
                Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                Properties = new Dictionary<string, JToken>()
            }
        };

        var options = CreateSetRepositoryMergePoliciesOptions(repository, branch, updatedMergePolicies, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify policies were updated (not appended)
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath);
        var branchPolicies = await DeserializeBranchMergePoliciesAsync(fullPath);
        branchPolicies.Should().HaveCount(1);

        var updatedPolicy = branchPolicies[0];
        updatedPolicy.Repository.Should().Be(repository);
        updatedPolicy.Branch.Should().Be(branch);
        updatedPolicy.MergePolicies.Should().HaveCount(2);
        updatedPolicy.MergePolicies.Should().Contain(p => p.Name == MergePolicyConstants.AllCheckSuccessfulMergePolicyName);
        updatedPolicy.MergePolicies.Should().Contain(p => p.Name == MergePolicyConstants.NoRequestedChangesMergePolicyName);
    }

    private void SetupGetRepositoryMergePoliciesAsync(string repository, string branch, IEnumerable<MergePolicy>? policies) => BarClientMock
            .Setup(x => x.GetRepositoryMergePoliciesAsync(repository, branch))
            .Returns(Task.FromResult<IEnumerable<MergePolicy>>(policies!));

    private SetRepositoryMergePoliciesCommandLineOptions CreateSetRepositoryMergePoliciesOptions(
        string repository,
        string branch,
        List<MergePolicy> mergePolicies,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new SetRepositoryMergePoliciesCommandLineOptions
        {
            Repository = repository,
            Branch = branch,
            AllChecksSuccessfulMergePolicy = mergePolicies.Any(p => p.Name == MergePolicyConstants.AllCheckSuccessfulMergePolicyName),
            IgnoreChecks = mergePolicies
                .Where(p => p.Name == MergePolicyConstants.AllCheckSuccessfulMergePolicyName)
                .SelectMany(p => p.Properties.TryGetValue(MergePolicyConstants.IgnoreChecksMergePolicyPropertyName, out var value)
                    ? value.ToObject<List<string>>() ?? []
                    : [])
                .ToList(),
            NoRequestedChangesMergePolicy = mergePolicies.Any(p => p.Name == MergePolicyConstants.NoRequestedChangesMergePolicyName),
            DontAutomergeDowngradesMergePolicy = mergePolicies.Any(p => p.Name == MergePolicyConstants.DontAutomergeDowngradesPolicyName),
            StandardAutoMergePolicies = mergePolicies.Any(p => p.Name == MergePolicyConstants.StandardMergePolicyName),
            CodeFlowCheckMergePolicy = mergePolicies.Any(p => p.Name == MergePolicyConstants.CodeflowMergePolicyName),
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr,
            Quiet = true
        };
    }

    private SetRepositoryMergePoliciesOperation CreateOperation(SetRepositoryMergePoliciesCommandLineOptions options)
    {
        return new SetRepositoryMergePoliciesOperation(
            options,
            BarClientMock.Object,
            RemoteFactoryMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    /// <summary>
    /// Deserializes a YAML file containing a list of branch merge policies.
    /// </summary>
    private static async Task<List<BranchMergePoliciesYaml>> DeserializeBranchMergePoliciesAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<BranchMergePoliciesYaml>>(content) ?? [];
    }
}
