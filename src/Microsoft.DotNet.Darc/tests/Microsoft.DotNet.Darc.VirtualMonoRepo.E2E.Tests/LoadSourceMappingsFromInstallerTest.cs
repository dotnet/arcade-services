// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NuGet.Configuration;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class LoadSourceMappingsFromInstallerTest : VmrTestsBase
{
    [Test]
    public async Task InitializeTest()
    {
        var sourceMappingsPath = InstallerRepoPath / "src" / "SourceBuild" / "content" / "source-mappings.json";
        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath, sourceMappingsPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / "some-file.txt",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / "src" / "SourceBuild" / "content" / "source-mappings.json",
            VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName)
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);

        var sourceMappings = new SourceMappingFile
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
                    DefaultRemote = InstallerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
                }
            }
        };

        var settings = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };

        Directory.CreateDirectory(InstallerRepoPath / "src" / "SourceBuild" / "content");

        File.WriteAllText(InstallerRepoPath / "src" / "SourceBuild" / "content" / "source-mappings.json",
            JsonSerializer.Serialize(sourceMappings, settings));

        await GitOperations.CommitAll(InstallerRepoPath, "Add source mappings");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);
        
        File.WriteAllText(VmrPath / DarcLib.VirtualMonoRepo.VmrInfo.SourcesDir / "some-file.txt",
            "Some file");

        await GitOperations.CommitAll(VmrPath, "Add source mappings");
    }
}

