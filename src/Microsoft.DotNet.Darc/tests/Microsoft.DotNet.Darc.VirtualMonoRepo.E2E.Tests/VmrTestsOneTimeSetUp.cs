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


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[SetUpFixture]
public class VmrTestsOneTimeSetUp
{
    public static readonly LocalPath TestsDirectory;
    public static readonly LocalPath CommonVmrPath;
    public static readonly LocalPath CommonProductRepoPath;
    public static readonly LocalPath FirstInstallerDependencyPath;
    public static readonly LocalPath SecondInstallerDependencyPath;
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
        CommonProductRepoPath = TestsDirectory / Constants.ProductRepoName;
        CommonExternalRepoPath = TestsDirectory / Constants.SubmoduleRepoName;
        CommonDependencyPath = TestsDirectory / Constants.DependencyRepoName;
        CommonInstallerPath = TestsDirectory / Constants.InstallerRepoName;
        FirstInstallerDependencyPath = TestsDirectory / Constants.FirstInstallerDependencyName;
        SecondInstallerDependencyPath = TestsDirectory / Constants.SecondInstallerDependencyName;
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Directory.CreateDirectory(TestsDirectory);
        
        Directory.CreateDirectory(TestsDirectory / Constants.VmrName);
        Directory.CreateDirectory(TestsDirectory / Constants.VmrName / VmrInfo.SourcesDir);
        await _gitOperations.InitialCommit(TestsDirectory / Constants.VmrName);

        var repoDependencies = new Dictionary<string, List<Dependency>>
        {
            {Constants.ProductRepoName,  new List<Dependency> {new Dependency(Constants.DependencyRepoName, CommonDependencyPath) } }
        };

        await CreateRepositoryRecursive(CommonProductRepoPath, Constants.ProductRepoName, repoDependencies);
        File.Copy(ResourcesPath / Constants.ProductRepoFileName, CommonProductRepoPath / Constants.ProductRepoFileName, true);
        await _gitOperations.CommitAll(CommonProductRepoPath, "change file content");

        await CreateRepositoryRecursive(CommonExternalRepoPath, Constants.SubmoduleRepoName);

        var dependenciesMap = new Dictionary<string, List<Dependency>>
        {
            {
                Constants.InstallerRepoName,  
                new List<Dependency> 
                {
                    new Dependency(Constants.FirstInstallerDependencyName, FirstInstallerDependencyPath),
                    new Dependency(Constants.SecondInstallerDependencyName, SecondInstallerDependencyPath)
                } 
            },
            {Constants.FirstInstallerDependencyName, new List<Dependency> {new Dependency(Constants.DependencyRepoName, CommonDependencyPath) }},
            {Constants.SecondInstallerDependencyName, new List<Dependency> {new Dependency(Constants.DependencyRepoName, CommonDependencyPath) }},
        };

        await CreateRepositoryRecursive(CommonInstallerPath, Constants.InstallerRepoName, dependenciesMap);
        Directory.CreateDirectory(CommonInstallerPath / Constants.PatchesFolderName / Constants.ProductRepoName);
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

    private async Task<string> CreateRepositoryRecursive(
        LocalPath repoPath,
        string repoName,
        IDictionary<string, List<Dependency>>? dependencies = null)
    {
        if (dependencies != null && dependencies.ContainsKey(repoName))
        {
            var repoDependencies = dependencies[repoName];
            foreach (var dep in repoDependencies)
            {
                if (!Directory.Exists(dep.Uri))
                {
                    await CreateRepositoryRecursive(dep.Uri, dep.Name, dependencies);
                }
            }
        }

        if (!Directory.Exists(repoPath))
        {
            Directory.CreateDirectory(repoPath);
            Directory.CreateDirectory(repoPath / "eng");

            File.WriteAllText(repoPath / $"{repoName}-file.txt", $"File in {repoName}");
            
            await _gitOperations.InitialCommit(repoPath);
        }
        
        return await _gitOperations.GetRepoLastCommit(repoPath);
    }
}
