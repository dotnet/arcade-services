// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
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
        string runtimeSha = "26a71c61fbda229f151afb14e274604b4926df5c";
        string runtimeVersion = "7.0.0-rc.1.22403.8";

        var allVersionsPropsFile = new AllVersionsPropsFile(new()
        {
            { "runtimeGitCommitHash", runtimeSha },
            { "runtimeOutputPackageVersion", runtimeVersion },
            { "sdkGitCommitHash", "6e00e543bbeb8e0491420e2f6b3f7d235166596d" },
            { "sdkOutputPackageVersion", "7.0.100-rc.1.22404.18" },
        });

        void VerifyVersions()
        {
            allVersionsPropsFile.GetVersion("runtime").Should().Be((runtimeSha, runtimeVersion));
            allVersionsPropsFile.GetVersion("sdk").Should().Be(("6e00e543bbeb8e0491420e2f6b3f7d235166596d", "7.0.100-rc.1.22404.18"));
        }
        
        VerifyVersions();

        allVersionsPropsFile.SerializeToXml(_outputFile ?? throw new Exception("Output file is not initialized"));

        var content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{runtimeSha}</runtimeGitCommitHash>
                <runtimeOutputPackageVersion>{runtimeVersion}</runtimeOutputPackageVersion>
                <sdkGitCommitHash>6e00e543bbeb8e0491420e2f6b3f7d235166596d</sdkGitCommitHash>
                <sdkOutputPackageVersion>7.0.100-rc.1.22404.18</sdkOutputPackageVersion>
              </PropertyGroup>
            </Project>
            """);

        allVersionsPropsFile = AllVersionsPropsFile.DeserializeFromXml(_outputFile);

        VerifyVersions();

        var newRuntimeSha = "225ce682f0578db2db5644df5e7024276b39785e";
        var newRuntimeVersion = "7.0.0-rc.1.22403.8";
        allVersionsPropsFile.UpdateVersion("runtime", newRuntimeSha, newRuntimeVersion);

        allVersionsPropsFile.GetVersion("runtime").Should().Be((newRuntimeSha, newRuntimeVersion));
        allVersionsPropsFile.GetVersion("sdk").Should().Be(("6e00e543bbeb8e0491420e2f6b3f7d235166596d", "7.0.100-rc.1.22404.18"));

        allVersionsPropsFile.SerializeToXml(_outputFile);

        content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{newRuntimeSha}</runtimeGitCommitHash>
                <runtimeOutputPackageVersion>{newRuntimeVersion}</runtimeOutputPackageVersion>
                <sdkGitCommitHash>6e00e543bbeb8e0491420e2f6b3f7d235166596d</sdkGitCommitHash>
                <sdkOutputPackageVersion>7.0.100-rc.1.22404.18</sdkOutputPackageVersion>
              </PropertyGroup>
            </Project>
            """);
    }
}
