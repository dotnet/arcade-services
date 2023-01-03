// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);

        _sourceMappings = new SourceMappingFile
        {
            PatchesPath = "src/installer/patches/",
            SourceMappings = "src/installer/src/SourceBuild/content/source-mappings.json",
            AdditionalMappings = new List<AdditionalMappingSetting>
            {
                new AdditionalMappingSetting
                {
                    Source = "src/installer/src/SourceBuild/content/",
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
                }
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

