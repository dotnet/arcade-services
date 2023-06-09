// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class VersionFilesTests
{
    [Test]
    public void FinalizedVersionIsDerivedTest()
    {
        VersionFiles.DeriveBuildInfo("runtime", "3.24.1")
            .Should().Be((DateTime.Now.ToString("yyyyMMdd.1"), ""));
    }

    [Test]
    public void NugetVersionIsDerivedTest()
    {
        VersionFiles.DeriveBuildInfo("nuget-client", "6.4.0-preview.1.51")
            .Should().Be((DateTime.Now.ToString("yyyyMMdd.1"), "preview"));
    }

    [Test]
    public void OldPreviewVersionIsDerivedTest()
    {
        VersionFiles.DeriveBuildInfo("format", "17.4.0-beta-22213-01")
            .Should().Be((DateTime.Now.ToString("20220413.1"), "beta"));
    }

    [Test]
    public void NewPreviewVersionIsDerivedTest()
    {
        VersionFiles.DeriveBuildInfo("arcade", "5.0.0-preview.7.20365.12")
            .Should().Be(("20200715.12", "preview.7"));
    }
}
