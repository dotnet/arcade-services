// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

internal abstract class CodeFlowTests : CodeFlowTestsBase
{
    protected const string FakePackageName = "Fake.Package";
    protected const string FakePackageVersion = "1.0.0";

    protected readonly string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);

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
            [
                _productRepoVmrFilePath,
                _productRepoVmrPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1",
                VmrPath / VersionFiles.GlobalJson]);

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

    protected static List<DependencyDetail> GetDependencies(ProductConstructionService.Client.Models.Build build)
        => build.Assets
            .Select(a => new DependencyDetail
            {
                Name = a.Name,
                Version = a.Version,
                RepoUri = build.GitHubRepository,
                Commit = build.Commit,
                Type = a.Name == DependencyFileManager.ArcadeSdkPackageName ? DependencyType.Toolset : DependencyType.Product,
                Pinned = false,
            })
            .ToList();

    protected async Task<CodeFlowResult> ChangeRepoFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'");

        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);

        if (!codeFlowResult.ConflictedFiles.Any(f => f.Path == VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName) / _productRepoFileName))
        {
            CheckFileContents(_productRepoVmrFilePath, newContent);
        }

        return codeFlowResult;
    }

    protected async Task<CodeFlowResult> ChangeVmrFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / _productRepoFileName, newContent);
        await GitOperations.CommitAll(VmrPath, $"Changing a VMR file to '{newContent}'");

        var codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);

        if (!codeFlowResult.ConflictedFiles.Any(f => f.Path == _productRepoFileName))
        {
            CheckFileContents(_productRepoFilePath, newContent);
        }

        return codeFlowResult;
    }

    protected async Task VerifyDependenciesInRepo(NativePath repo, List<DependencyDetail> expectedDependencies)
    {
        var dependencies = await GetLocal(repo)
            .GetDependenciesAsync();

        dependencies
            .Where(d => d.Type == DependencyType.Product)
            .Should().BeEquivalentTo(expectedDependencies);

        var versionDetailsProps = await File.ReadAllTextAsync(repo / VersionFiles.VersionDetailsProps);
        foreach (var dependency in expectedDependencies)
        {
            var tagName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
            versionDetailsProps.Should().Contain($"<{tagName}>{dependency.Version}</{tagName}>");
        }
    }

    protected async Task VerifyDependenciesInVmrRepo(string repoName, List<DependencyDetail> expectedDependencies)
    {
        await VerifyDependenciesInRepo(VmrPath / VmrInfo.SourcesDir / repoName, expectedDependencies);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        CopyDirectory(CodeflowTestsOneTimeSetUp.TestsDirectory / Constants.SecondRepoName, SecondRepoPath);

        await CopyRepoAndCreateVersionFiles(Constants.ProductRepoName);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        var repoPath = CurrentTestDirectory / "vmr";
        CopyDirectory(CodeflowTestsOneTimeSetUp.TestsDirectory / "vmr", repoPath);

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

        await File.WriteAllTextAsync(VmrPath / VersionFiles.GlobalJson, Constants.VmrBaseGlobalJsonTemplate);
        await GitOperations.CommitAll(VmrPath, "Create global json in vmr`s base");
    }

    protected async Task<string[]> CallDarcForwardflow(int buildId = 0, string[]? expectedConflicts = null)
        => await CallDarcCodeFlowOperation<ForwardFlowOperation>(
            new ForwardFlowCommandLineOptions
            {
                VmrPath = VmrPath,
                TmpPath = TmpPath,
                Build = buildId,
            },
            ProductRepoPath,
            VmrPath,
            expectedConflicts);

    protected async Task<string[]> CallDarcBackflow(int buildId = 0, string[]? expectedConflicts = null)
        => await CallDarcCodeFlowOperation<BackflowOperation>(
            new BackflowCommandLineOptions
            {
                VmrPath = VmrPath,
                TmpPath = TmpPath,
                Repository = ProductRepoPath,
                Build = buildId,
            },
            VmrPath,
            ProductRepoPath,
            expectedConflicts);

    private async Task<string[]> CallDarcCodeFlowOperation<T>(
            ICodeFlowCommandLineOptions options,
            NativePath sourceRepo,
            NativePath targetRepo,
            string[]? expectedConflicts)
        where T : CodeFlowOperation
    {

        var operation = ActivatorUtilities.CreateInstance<T>(ServiceProvider, options);
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(sourceRepo);
        try
        {
            var result = await operation.ExecuteAsync();

            if (expectedConflicts == null || expectedConflicts.Length == 0)
            {
                result.Should().Be(0);
            }
            else
            {
                result.Should().Be(42);

                var diff = await GitOperations.ExecuteGitCommand(targetRepo, ["diff", "--name-status"]);

                foreach (var expectedFile in expectedConflicts)
                {
                    diff.StandardOutput.Should().MatchRegex(new Regex(@$"^U\s+{Regex.Escape(expectedFile)}[\n\r]+", RegexOptions.Multiline));
                }
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        var gitResult = await GitOperations.ExecuteGitCommand(targetRepo, "diff", "--name-only", "--cached");
        gitResult.Succeeded.Should().BeTrue("Git diff should succeed");
        return gitResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}

internal static class BackFlowTestExtensions
{
    public static void ShouldHaveUpdates(this CodeFlowResult codeFlowResult)
        => VerifyUpdates(codeFlowResult, true, "new code flow updates are expected");

    public static void ShouldNotHaveUpdates(this CodeFlowResult codeFlowResult)
        => VerifyUpdates(codeFlowResult, false, "no updates are expected");

    private static void VerifyUpdates(CodeFlowResult codeFlowResult, bool expected, string message)
    {
        codeFlowResult.HadUpdates.Should().Be(expected, message);
    }
}

