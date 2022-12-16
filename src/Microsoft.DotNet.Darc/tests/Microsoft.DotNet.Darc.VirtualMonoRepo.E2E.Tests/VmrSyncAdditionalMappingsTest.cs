// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrSyncAdditionalMappingsTest : VmrTestsBase
{
    private readonly string _repoName = "special-repo";
    private readonly string _fileName = "special-file.txt";
    private readonly string _fileRelativePath = new NativePath("content") / "special-file.txt";
    private string _filePath = null!;

    [Test]
    public async Task NonSrcContentIsSyncedTest()
    {
        // Initialize the repo

        await InitializeRepoAtLastCommit(_repoName, _specialRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / VmrInfo.SourcesDir / _repoName / _fileRelativePath,
            _vmrPath / _fileName
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { _repoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // Change a file in the mapped folder

        File.WriteAllText(_filePath, "A file with a change that needs to be copied outside of the src folder");
        await GitOperations.CommitAll(_specialRepoPath, "Change file");
        await UpdateRepoToLastCommit(_repoName, _specialRepoPath);

        CheckFileContents(_vmrPath / _fileName, "A file with a change that needs to be copied outside of the src folder");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        _filePath = _specialRepoPath / _fileRelativePath;

        Directory.CreateDirectory(_specialRepoPath);
        Directory.CreateDirectory(_specialRepoPath / "content");
        Directory.CreateDirectory(_specialRepoPath / "eng");
        File.WriteAllText(
            _filePath,
            "A file that needs to be copied outside of the src folder");
        File.WriteAllText(_specialRepoPath / VersionFiles.VersionDetailsXml, Constants.EmptyVersionDetails);

        await GitOperations.InitialCommit(_specialRepoPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = "special-repo",
                    DefaultRemote = _specialRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = _privateRepoPath
                }
            },
            AdditionalMappings = new List<AdditionalMappingSetting>
            {
                new AdditionalMappingSetting
                {
                    Source = "src/special-repo/content",
                    Destination = ""
                }
            }
        };

        sourceMappings.Defaults.Exclude = new[]
        {
            "externals/external-repo/**/*.exe",
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
