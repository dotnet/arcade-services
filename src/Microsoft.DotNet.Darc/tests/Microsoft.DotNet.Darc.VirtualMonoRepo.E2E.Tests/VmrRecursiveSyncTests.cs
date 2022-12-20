// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrRecursiveSyncTests : VmrTestsBase
{
    private LocalPath _aspnetcorePath = null!;
    private LocalPath _runtimePath = null!;

    protected override async Task CopyReposForCurrentTest()
    {
        _aspnetcorePath = _currentTestDirectory / Constants.FirstInstallerDependencyName;
        _runtimePath = _currentTestDirectory / Constants.SecondInstallerDependencyName;

        var dependenciesMap = new Dictionary<string, List<Dependency>>
        {
            {
                Constants.InstallerRepoName,
                new List<Dependency>
                {
                    new Dependency(Constants.FirstInstallerDependencyName, _aspnetcorePath),
                    new Dependency(Constants.SecondInstallerDependencyName, _runtimePath)
                }
            },
            {Constants.FirstInstallerDependencyName, new List<Dependency> {new Dependency(Constants.DependencyRepoName, _dependencyRepoPath) }},
            {Constants.SecondInstallerDependencyName, new List<Dependency> {new Dependency(Constants.DependencyRepoName, _dependencyRepoPath) }},
        };

        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.InstallerRepoName, dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = _installerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.FirstInstallerDependencyName,
                    DefaultRemote = _aspnetcorePath
                },
                new SourceMappingSetting
                {
                    Name = Constants.SecondInstallerDependencyName,
                    DefaultRemote = _runtimePath
                },
                new SourceMappingSetting
                {
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = _dependencyRepoPath
                }
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }

    [Test]
    public void Test()
    {
        var str = "hhh";
    }

}
