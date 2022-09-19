// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;

[TestFixture]
public class AllVersionsPropsFileTests
{
    private string? _outputFile;

    [SetUp]
    public void SetUpOutputFile()
    {
        _outputFile = Path.GetTempFileName();
    }

    [TearDown]
    public void CleanUpOutputFile()
    {
        try
        {
            if (_outputFile is not null)
            {
                File.Delete(_outputFile);
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Test]
    public void AllVersionsPropsFileIsDeSerializedTest()
    {
        var runtimeVersion = new VmrDependencyVersion("26a71c61fbda229f151afb14e274604b4926df5c", "7.0.0-rc.1.22403.8");
        var sdkVersion = new VmrDependencyVersion("6e00e543bbeb8e0491420e2f6b3f7d235166596d", "7.0.100-rc.1.22404.18");

        var allVersionsPropsFile = new AllVersionsPropsFile(new()
        {
            { "runtimeGitCommitHash", runtimeVersion.Sha },
            { "runtimeOutputPackageVersion", runtimeVersion.PackageVersion! },
            { "sdkGitCommitHash", sdkVersion.Sha },
            { "sdkOutputPackageVersion", sdkVersion.PackageVersion! },
        });

        void VerifyVersions()
        {
            allVersionsPropsFile.GetVersion("runtime").Should().Be(runtimeVersion);
            allVersionsPropsFile.GetVersion("sdk").Should().Be(sdkVersion);
        }
        
        VerifyVersions();

        allVersionsPropsFile.SerializeToXml(_outputFile ?? throw new Exception("Output file is not initialized"));

        var content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{runtimeVersion.Sha}</runtimeGitCommitHash>
                <runtimeOutputPackageVersion>{runtimeVersion.PackageVersion}</runtimeOutputPackageVersion>
                <sdkGitCommitHash>{sdkVersion.Sha}</sdkGitCommitHash>
                <sdkOutputPackageVersion>{sdkVersion.PackageVersion}</sdkOutputPackageVersion>
              </PropertyGroup>
            </Project>
            """);

        allVersionsPropsFile = AllVersionsPropsFile.DeserializeFromXml(_outputFile);

        VerifyVersions();

        runtimeVersion = new VmrDependencyVersion("225ce682f0578db2db5644df5e7024276b39785e", "7.0.0-rc.1.22444.8");

        allVersionsPropsFile.UpdateVersion("runtime", runtimeVersion.Sha, runtimeVersion.PackageVersion!);

        allVersionsPropsFile.GetVersion("runtime").Should().Be(runtimeVersion);
        allVersionsPropsFile.GetVersion("sdk").Should().Be(sdkVersion);

        allVersionsPropsFile.SerializeToXml(_outputFile);

        content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{runtimeVersion.Sha}</runtimeGitCommitHash>
                <runtimeOutputPackageVersion>{runtimeVersion.PackageVersion}</runtimeOutputPackageVersion>
                <sdkGitCommitHash>{sdkVersion.Sha}</sdkGitCommitHash>
                <sdkOutputPackageVersion>{sdkVersion.PackageVersion}</sdkOutputPackageVersion>
              </PropertyGroup>
            </Project>
            """);
    }
}
