// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[SetUpFixture]
public class VmrTestsOneTimeSetUp
{
    public static readonly LocalPath TestsDirectory;
    public static readonly LocalPath CommonVmrPath;
    public static readonly LocalPath CommonPrivateRepoPath;
    public static readonly LocalPath CommonDependencyPath;
    public static readonly LocalPath CommonInstallerPath;
    public static readonly LocalPath CommonExternalRepoPath;
    public static readonly LocalPath ResourcesPath;
    private GitOperationsHelper _gitOperations { get; } = new();


    static VmrTestsOneTimeSetUp()
    {
        var assembly = Assembly.GetAssembly(typeof(VmrTestsBase)) ?? throw new Exception("Assembly not found");
        ResourcesPath = new NativePath(Path.Join(Path.GetDirectoryName(assembly.Location), "Resources"));
        TestsDirectory = new NativePath(Path.GetTempPath()) / "_vmrTests" / Path.GetRandomFileName();
        CommonVmrPath = TestsDirectory / Constants.VmrName;
        CommonPrivateRepoPath = TestsDirectory / Constants.ProductRepoName;
        CommonExternalRepoPath = TestsDirectory / Constants.SubmoduleRepoName;
        CommonDependencyPath = TestsDirectory / Constants.DependencyRepoName;
        CommonInstallerPath = TestsDirectory / Constants.InstallerRepoName;
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Directory.CreateDirectory(TestsDirectory);
        
        Directory.CreateDirectory(TestsDirectory / Constants.VmrName);
        Directory.CreateDirectory(TestsDirectory / Constants.VmrName / VmrInfo.SourcesDir);
        await _gitOperations.InitialCommit(TestsDirectory / Constants.VmrName);

        Directory.CreateDirectory(CommonInstallerPath);
        Directory.CreateDirectory(CommonInstallerPath / "eng");
        Directory.CreateDirectory(CommonInstallerPath / Constants.PatchesFolderName / Constants.ProductRepoName);

        File.WriteAllText(CommonInstallerPath / VersionFiles.VersionDetailsXml, Constants.EmptyVersionDetails);
        await _gitOperations.InitialCommit(CommonInstallerPath);

        var dependenciesMap = new Dictionary<string, List<Dependency>>
        {
            {Constants.ProductRepoName,  new List<Dependency> {new Dependency(Constants.DependencyRepoName, CommonDependencyPath) } }
        };

        await CreateRepositoryRecursive(CommonPrivateRepoPath, Constants.ProductRepoName, dependenciesMap);
        File.Copy(ResourcesPath / Constants.ProductRepoFileName, CommonPrivateRepoPath / Constants.ProductRepoFileName, true);
        await _gitOperations.CommitAll(CommonPrivateRepoPath, "change file content");

        await CreateRepositoryRecursive(CommonExternalRepoPath, Constants.SubmoduleRepoName);
    }

    [OneTimeTearDown]
    public void DeleteTestsDirectory()
    {
        try
        {
            if (TestsDirectory is not null)
            {
                DeleteDirectory(TestsDirectory);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static void DeleteDirectory(string targetDir)
    {
        File.SetAttributes(targetDir, FileAttributes.Normal);

        string[] files = Directory.GetFiles(targetDir);
        string[] dirs = Directory.GetDirectories(targetDir);

        foreach (string file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (string dir in dirs)
        {
            DeleteDirectory(dir);
        }

        Directory.Delete(targetDir, false);
    }

    protected async Task<string> CreateRepositoryRecursive(
        LocalPath repoPath,
        string repoName,
        IDictionary<string, List<Dependency>>? dependencies = null,
        bool createVersionDetails = false)
    {
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(repoPath / "eng");

        var dependenciesString = new StringBuilder();
        if (dependencies != null && dependencies.ContainsKey(repoName))
        {
            var repoDependencies = dependencies[repoName];
            foreach (var dep in repoDependencies)
            {
                if (!Directory.Exists(dep.Uri))
                {
                    string sha = await CreateRepositoryRecursive(dep.Uri, dep.Name, dependencies);
                    if (createVersionDetails)
                    {
                        dependenciesString.AppendLine(
                            string.Format(
                                Constants.DependencyTemplate,
                                new[] { dep.Name, VmrTestsBase.EscapePath(dep.Uri), sha }));
                    }
                }
            }
        }

        File.WriteAllText(repoPath / $"{repoName}-file.txt", $"File in {repoName}");
        if (createVersionDetails)
        {
            var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependenciesString);
            File.WriteAllText(repoPath / VersionFiles.VersionDetailsXml, versionDetails);
        }

        await _gitOperations.InitialCommit(repoPath);
        return await _gitOperations.GetRepoLastCommit(repoPath);
    }
}
