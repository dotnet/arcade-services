// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class LoadSourceMappingsFromInstallerTest : VmrTestsBase
{
    private SourceMappingFile _sourceMappings = null!;
    private readonly JsonSerializerOptions _jsonSettings;
    private readonly LocalPath _sourceMappingsRelativePath = 
        new NativePath("src") / "SourceBuild" / "content" / "source-mappings.json";

    public LoadSourceMappingsFromInstallerTest()
    {
        _jsonSettings = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };
    }

    [Test]
    public async Task LoadCorrectSourceMappingsVersionTest()
    {
        var sourceMappingsPath = InstallerRepoPath / _sourceMappingsRelativePath;
        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath, sourceMappingsPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / _sourceMappingsRelativePath,
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName)
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        //Update the mapping to exclude .exe files and add a new .exe file into the repo at the same time
        //the file shouldn't be ingested into the VMR

        _sourceMappings.Mappings = new List<SourceMappingSetting>
        {
            new SourceMappingSetting
            {
                Name = Constants.InstallerRepoName,
                DefaultRemote = InstallerRepoPath,
                Exclude = new[] { "src/*.dll", "src/*.exe" }
            }
        };

        File.WriteAllText(InstallerRepoPath / _sourceMappingsRelativePath,
            JsonSerializer.Serialize(_sourceMappings, _jsonSettings));
        File.WriteAllText(InstallerRepoPath / "src" / "excluded.exe", "Excluded exe file");

        await GitOperations.CommitAll(InstallerRepoPath, "Change source-mappings");

        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
    }

    [Test]
    public async Task NewRepoAddedDuringSyncTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);

        var sourceMappingsPath = InstallerRepoPath / _sourceMappingsRelativePath;
        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath, sourceMappingsPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / _sourceMappingsRelativePath,
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName),
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Now we will add a new dependency in the installer repo's Version.Details.xml
        var dependencies = string.Format(
            Constants.DependencyTemplate,
            new[] { Constants.ProductRepoName, ProductRepoPath, await GitOperations.GetRepoLastCommit(ProductRepoPath) });
        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependencies);
        File.WriteAllText(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);

        // We will also only add the new mapping with the dependency. This should verify we're syncing the new source-mappings file
        _sourceMappings.Mappings.Add(
            new SourceMappingSetting
            {
                Name = Constants.ProductRepoName,
                DefaultRemote = ProductRepoPath
            });

        File.WriteAllText(
            InstallerRepoPath / _sourceMappingsRelativePath,
            JsonSerializer.Serialize(_sourceMappings, _jsonSettings));

        await GitOperations.CommitAll(InstallerRepoPath, "Added new dependency");

        // We will sync new installer, which should bring in the new product repo
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[]
            {
                Constants.InstallerRepoName,
                Constants.ProductRepoName,
            },
            expectedFilesFromRepos);

        expectedFiles.Add(VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName));

        CheckDirectoryContents(VmrPath, expectedFiles);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);

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
                    Exclude = new[] { "src/*.dll" }
                },
            }
        };

        Directory.CreateDirectory(InstallerRepoPath / "src" / "SourceBuild" / "content");

        File.WriteAllText(InstallerRepoPath / _sourceMappingsRelativePath,
            JsonSerializer.Serialize(_sourceMappings, _jsonSettings));

        File.WriteAllText(InstallerRepoPath / "src" / "forbidden.dll", "Ignored file");
       
        await GitOperations.CommitAll(InstallerRepoPath, "Add files");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);
        
        File.WriteAllText(VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            "Some file");

        await GitOperations.CommitAll(VmrPath, "Add source mappings");
    }
}

