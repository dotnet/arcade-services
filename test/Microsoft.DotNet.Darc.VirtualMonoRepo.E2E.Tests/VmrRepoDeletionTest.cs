// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrRepoDeletionTest : VmrTestsBase
{
    private SourceMappingFile _sourceMappings = null!;
    private readonly JsonSerializerOptions _jsonSettings;
    private readonly LocalPath _sourceMappingsRelativePath =
        new NativePath("src") / "SourceBuild" / "content" / "source-mappings.json";

    public VmrRepoDeletionTest()
    {
        _jsonSettings = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };
    }

    [Test]
    public async Task RepoIsDeletedFromVmrTest()
    {
        var sourceMappingsPath = InstallerRepoPath / _sourceMappingsRelativePath;
        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath, sourceMappingsPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath, sourceMappingsPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / _sourceMappingsRelativePath,
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName),
            VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName)
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName, Constants.ProductRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Remove product-repo1 mapping

        _sourceMappings.Mappings = new List<SourceMappingSetting>
        {
            new SourceMappingSetting
            {
                Name = Constants.InstallerRepoName,
                DefaultRemote = InstallerRepoPath,
            }
        };

        await File.WriteAllTextAsync(InstallerRepoPath / _sourceMappingsRelativePath,
            JsonSerializer.Serialize(_sourceMappings, _jsonSettings));

        await GitOperations.CommitAll(InstallerRepoPath, "Change source-mappings");

        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / _sourceMappingsRelativePath,
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName),
        };

        expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        var versions = AllVersionsPropsFile.DeserializeFromXml(VmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName);
        versions.Versions.Keys.Should().BeEquivalentTo(new string[] { "installerGitCommitHash" });

        var sourceManifest = SourceManifest.FromJson(Info.GetSourceManifestPath());
        sourceManifest.Repositories.Should().HaveCount(1);
        sourceManifest.Repositories.First().Path.Should().Be("installer");

        await GitOperations.CheckAllIsCommitted(VmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);

        _sourceMappings = new SourceMappingFile
        {
            PatchesPath = "src/installer/patches/",
            SourceMappingsPath = "src/installer/src/SourceBuild/content/source-mappings.json",
            AdditionalMappings = new List<AdditionalMappingSetting>
            {
                new AdditionalMappingSetting
                {
                    Source = "src/installer/src/SourceBuild/content/source-mappings.json",
                    Destination = "src"
                }
            },
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = InstallerRepoPath,
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath,
                },
            }
        };

        Directory.CreateDirectory(InstallerRepoPath / "src" / "SourceBuild" / "content");

        await File.WriteAllTextAsync(InstallerRepoPath / _sourceMappingsRelativePath,
            JsonSerializer.Serialize(_sourceMappings, _jsonSettings));

        await GitOperations.CommitAll(InstallerRepoPath, "Add files");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        await File.WriteAllTextAsync(VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            "Some file");

        await GitOperations.CommitAll(VmrPath, "Add source mappings");
    }
}
