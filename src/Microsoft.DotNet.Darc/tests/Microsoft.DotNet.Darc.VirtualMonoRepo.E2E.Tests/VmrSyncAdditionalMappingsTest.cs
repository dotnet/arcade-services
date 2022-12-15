// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrSyncAdditionalMappingsTest : VmrTestsBase
{
    private readonly string _repoName = "special-repo";
    private string _filePath = null!;

    [Test]
    public async Task NonSrcContentIsSyncedTest()
    {
        var versionDetailsPath = _specialRepoPath / "eng" / Constants.VersionDetailsName;
        var sourceMappingsPath = _vmrPath / "src" / "source-mappings.json";

        // Initialize the repo

        await InitializeRepoAtLastCommit(_repoName, _specialRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / "src" / _repoName / "content" / "special-file.txt",
            _vmrPath / "special-file.txt"
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

        CheckFileContents(_vmrPath / "special-file.txt", "A file with a change that needs to be copied outside of the src folder");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        _filePath = _specialRepoPath / "content" / "special-file.txt";

        Directory.CreateDirectory(_specialRepoPath);
        Directory.CreateDirectory(_specialRepoPath / "content");
        Directory.CreateDirectory(_specialRepoPath / "eng");
        File.WriteAllText(
            _filePath,
            "A file that needs to be copied outside of the src folder");
        File.WriteAllText(_specialRepoPath / "eng" / Constants.VersionDetailsName, Constants.EmptyVersionDetails);

        await GitOperations.InitialCommit(_specialRepoPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var mappings = new List<SourceMapping>
        {
            new SourceMapping("special-repo", _specialRepoPath.Path.Replace("\\", "\\\\")),
            new SourceMapping(
                "test-repo",
                _privateRepoPath.Path.Replace("\\", "\\\\"),
                new List<string> { "externals/external-repo/**/*.exe", "excluded/*" })
        };

        var additionalMappings = new List<AdditionalMapping>
        {
            new AdditionalMapping("src/special-repo/content", "")
        };

        var sm = GenerateSourceMappings(mappings, "", additionalMappings);

        File.WriteAllText(
            _vmrPath / "src" / "source-mappings.json",
            sm);

        await GitOperations.CommitAll(_vmrPath, "Replace source mappings");
    }
}
