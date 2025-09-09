// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Models.Darc;

[TestFixture]
public class AssetMatcherTests
{
    [Test]
    public void GetAssetMatchers_WithMultipleDirectories_ReturnsCorrectMatchers()
    {
        var filters = new List<string>
        {
            "src/sdk/Package1", // should be in path2
            "src/*/Package2",  // should be in path2 and path3
            "Package3",      // should be in path1
            "*/Package4" // should be in all paths
        };
        UnixPath path1 = new(".");
        UnixPath path2 = new("src/sdk");
        UnixPath path3 = new("src/templating");
        var directories = new List<UnixPath> { path1, path2, path3 };

        var matchers = filters.GetAssetMatchersPerDirectory(directories);

        matchers[path1].IsExcluded("Package3").Should().BeTrue();
        matchers[path1].IsExcluded("Package1").Should().BeFalse();
        matchers[path2].IsExcluded("Package1").Should().BeTrue();
        matchers[path2].IsExcluded("Package2").Should().BeTrue();
        matchers[path2].IsExcluded("Package3").Should().BeFalse();
        matchers[path3].IsExcluded("Package2").Should().BeTrue();
        matchers[path1].IsExcluded("Package4").Should().BeTrue();
        matchers[path2].IsExcluded("Package4").Should().BeTrue();
        matchers[path3].IsExcluded("Package4").Should().BeTrue();
    }
}
