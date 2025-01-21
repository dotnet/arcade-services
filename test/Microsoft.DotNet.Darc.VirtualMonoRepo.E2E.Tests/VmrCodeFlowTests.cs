// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

internal abstract class VmrCodeFlowTests : VmrTestsBase
{
    protected const string FakePackageName = "Fake.Package";
    protected const string FakePackageVersion = "1.0.0";

    protected readonly string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
    private readonly Mock<IBasicBarClient> _basicBarClient = new();

    protected NativePath _productRepoVmrPath = null!;
    protected NativePath _productRepoVmrFilePath = null!;
    protected NativePath _productRepoFilePath = null!;
    protected NativePath _productRepoScriptFilePath = null!;

    protected override IServiceCollection CreateServiceProvider()
        => base.CreateServiceProvider()
            .AddSingleton(_basicBarClient.Object);

    [SetUp]
    public void SetUp()
    {
        _productRepoVmrPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoVmrFilePath = _productRepoVmrPath / _productRepoFileName;
        _productRepoScriptFilePath = ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1";
        _productRepoFilePath = ProductRepoPath / _productRepoFileName;
        _basicBarClient.Reset();
    }

    protected async Task EnsureTestRepoIsInitialized()
    {
        var vmrSha = await GitOperations.GetRepoLastCommit(VmrPath);

        // Add some eng/common content into the repo
        Directory.CreateDirectory(Path.GetDirectoryName(_productRepoScriptFilePath)!);
        await File.WriteAllTextAsync(_productRepoScriptFilePath, "Some common script file");
        await GitOperations.CommitAll(ProductRepoPath, "Add eng/common file into the repo");

        // We populate Version.Details.xml with a fake package which we will flow back and forth
        await GetLocal(ProductRepoPath).AddDependencyAsync(new DependencyDetail
        {
            Name = FakePackageName,
            Version = FakePackageVersion,
            RepoUri = VmrPath,
            Commit = vmrSha,
            Type = DependencyType.Product,
            Pinned = false,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Adding a fake dependency");

        // We also add Arcade SDK so that we can verify eng/common updates
        await GetLocal(ProductRepoPath).AddDependencyAsync(new DependencyDetail
        {
            Name = DependencyFileManager.ArcadeSdkPackageName,
            Version = FakePackageVersion,
            RepoUri = VmrPath,
            Commit = vmrSha,
            Type = DependencyType.Toolset,
            Pinned = false,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Adding Arcade dependency");

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);
        await GitOperations.Checkout(ProductRepoPath, "main");

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName],
            [_productRepoVmrFilePath, _productRepoVmrPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1"]);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoVmrFilePath, _productRepoFileName);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        await File.WriteAllTextAsync(ProductRepoPath / _productRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a file in the repo");

        // Perform last VMR-lite-like forward flow
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoVmrFilePath, "Test changes in repo file");
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.Checkout(ProductRepoPath, "main");
    }

    protected static List<DependencyDetail> GetDependencies(Build build)
        => build.Assets
            .Select(a => new DependencyDetail
            {
                Name = a.Name,
                Version = a.Version,
                RepoUri = build.GitHubRepository,
                Commit = build.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            })
            .ToList();

    protected async Task<bool> ChangeRepoFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'");

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, newContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.Checkout(ProductRepoPath, "main");
        return hadUpdates;
    }

    protected async Task<bool> ChangeVmrFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / _productRepoFileName, newContent);
        await GitOperations.CommitAll(VmrPath, $"Changing a VMR file to '{newContent}'");

        var hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, newContent);
        return hadUpdates;
    }

    protected async Task VerifyDependenciesInRepo(NativePath repo, List<DependencyDetail> expectedDependencies)
    {
        var dependencies = await GetLocal(repo)
            .GetDependenciesAsync();

        dependencies
            .Where(d => d.Type == DependencyType.Product)
            .Should().BeEquivalentTo(expectedDependencies);

        var versionProps = await File.ReadAllTextAsync(repo / VersionFiles.VersionProps);
        foreach (var dependency in expectedDependencies)
        {
            var tagName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
            versionProps.Should().Contain($"<{tagName}>{dependency.Version}</{tagName}>");
        }
    }

    protected override async Task CopyReposForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / Constants.SecondRepoName, SecondRepoPath);

        await CopyRepoAndCreateVersionFiles(Constants.ProductRepoName);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        var repoPath = CurrentTestDirectory / "vmr";
        CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / "vmr", repoPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings =
            [
                new()
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath,
                }
            ]
        };

        sourceMappings.Defaults.Exclude =
        [
            "externals/external-repo/**/*.exe",
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        ];

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}

internal static class BackFlowTestExtensions
{
    public static void ShouldHaveUpdates(this bool hadUpdates)
        => VerifyUpdates(hadUpdates, true, "new code flow updates are expected");

    public static void ShouldNotHaveUpdates(this bool hadUpdates)
        => VerifyUpdates(hadUpdates, false, "no updates are expected");

    private static void VerifyUpdates(bool hadUpdates, bool expected, string message)
    {
        hadUpdates.Should().Be(expected, message);
    }
}

