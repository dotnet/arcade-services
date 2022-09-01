// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers
{
    [TestFixture]
    public class VersionFilesTests
    {
        [Test]
        public void FinalizedVersionIsDerived()
        {
            VersionFiles.GetDerivedVersionInfo("runtime", "3.24.1")
                .Should().Be((DateTime.Now.ToString("yyyyMMdd.1"), ""));
        }

        [Test]
        public void NugetVersionIsDerived()
        {
            VersionFiles.GetDerivedVersionInfo("nuget-client", "6.4.0-preview.1.51")
                .Should().Be((DateTime.Now.ToString("yyyyMMdd.1"), "preview"));
        }

        [Test]
        public void OldPreviewVersionIsDerived()
        {
            VersionFiles.GetDerivedVersionInfo("format", "17.4.0-preview-22213-01")
                .Should().Be((DateTime.Now.ToString("20220413.1"), "preview"));
        }

        [Test]
        public void VsTestVersionIsDerived()
        {
            VersionFiles.GetDerivedVersionInfo("vstest", "17.4.0-preview-20220813-01")
                .Should().Be((DateTime.Now.ToString("20220813.01"), "preview"));
        }

        [Test]
        public void NewPreviewVersionIsDerived()
        {
            VersionFiles.GetDerivedVersionInfo("arcade", "5.0.0-preview.7.20365.12")
                .Should().Be(("20200715.12", "preview.7"));
        }
    }
}
