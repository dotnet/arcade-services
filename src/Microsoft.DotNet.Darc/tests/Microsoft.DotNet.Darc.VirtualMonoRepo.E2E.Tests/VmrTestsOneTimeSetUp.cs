// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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
        CommonExternalRepoPath = TestsDirectory / Constants.SecondRepoName;
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

        await CreateRepository(CommonProductRepoPath, Constants.ProductRepoName, Constants.GetRepoFileName(Constants.ProductRepoName));
        await CreateRepository(CommonDependencyPath, Constants.DependencyRepoName);
        await CreateRepository(CommonExternalRepoPath, Constants.SecondRepoName);
        await CreateRepository(CommonInstallerPath, Constants.InstallerRepoName);
        Directory.CreateDirectory(CommonInstallerPath / Constants.PatchesFolderName / Constants.ProductRepoName);
    }

    [OneTimeTearDown]
    public void DeleteTestsDirectory()
    {
        //try
        //{
        //    if (TestsDirectory is not null)
        //    {
        //        DeleteDirectory(TestsDirectory);
        //    }
        //}
        //catch
        //{
        //    // Ignore
        //}
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

    private async Task CreateRepository(LocalPath repoPath, string repoName, string? resourceFileName = null)
    {
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(repoPath / "eng");

        if (resourceFileName != null)
        {
            File.Copy(ResourcesPath / resourceFileName, repoPath / Constants.GetRepoFileName(repoName));
        }
        else
        {
            File.WriteAllText(repoPath / Constants.GetRepoFileName(repoName), $"File in {repoName}");
        }

        await _gitOperations.InitialCommit(repoPath);
    }
}
