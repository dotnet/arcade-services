// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Models.Yaml;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Models.Yaml;

public class BranchOrderComparerTests
{
    [Test]
    public void BranchMergePolicies_SortsByBranchPriority()
    {
        // Arrange - Create branch merge policies with branches in random order
        List<BranchMergePoliciesYaml> unsortedPolicies =
        [
            new() { Repository = "https://github.com/test/repo", Branch = "feature/something" },
            new() { Repository = "https://github.com/test/repo", Branch = "release/8.0" },
            new() { Repository = "https://github.com/test/repo", Branch = "main" },
            new() { Repository = "https://github.com/test/repo", Branch = "internal/release/9.0" },
            new() { Repository = "https://github.com/test/repo", Branch = "master" },
            new() { Repository = "https://github.com/test/repo", Branch = "release/9.0" },
            new() { Repository = "https://github.com/test/repo", Branch = "dev" },
            new() { Repository = "https://github.com/test/repo", Branch = "internal/release/8.0" },
        ];

        // Act - Sort using the IComparable implementation
        var sorted = unsortedPolicies.Order().ToList();

        // Assert - Verify order: main, master, release/*, internal/release/*, then alphabetically
        sorted[0].Branch.Should().Be("main");
        sorted[1].Branch.Should().Be("master");
        sorted[2].Branch.Should().Be("release/8.0");
        sorted[3].Branch.Should().Be("release/9.0");
        sorted[4].Branch.Should().Be("internal/release/8.0");
        sorted[5].Branch.Should().Be("internal/release/9.0");
        sorted[6].Branch.Should().Be("dev");
        sorted[7].Branch.Should().Be("feature/something");
    }
}
