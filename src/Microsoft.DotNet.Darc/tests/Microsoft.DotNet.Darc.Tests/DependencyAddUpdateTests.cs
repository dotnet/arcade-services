// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests
{
    [TestFixture]
    public class DependencyAddUpdateTests
    {
        /// <summary>
        ///     Verifies that empty updates+rewrite don't do odd things.
        ///     Should format the xml to canonical form though.
        /// </summary>
        [Test]
        public async Task EmptyVersions1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(EmptyVersions1), async driver =>
            {
                await driver.UpdateDependenciesAsync(new List<DependencyDetail>());
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        ///     Verifies that non-empty versions don't get reformatted in odd ways.
        /// </summary>
        [Test]
        public async Task EmptyVersions2()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(EmptyVersions2), async driver =>
            {
                await driver.UpdateDependenciesAsync(new List<DependencyDetail>());
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add a basic dependency.  Versions.props has a default xmlns on the Project element.
        /// </summary>
        [Test]
        public async Task AddProductDependency1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency1), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.3",
                        Type = DependencyType.Product
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add a basic dependency.  Versions.props
        /// </summary>
        [Test]
        public async Task AddProductDependency2()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency2), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.3",
                        Type = DependencyType.Product
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add a dependency and then add it again.  Should throw on second add.
        /// 
        /// </summary>
        [Test]
        public async Task AddProductDependency3()
        {
            // Use assets from #2.
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency2), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.3",
                        Type = DependencyType.Product
                    });

                await (((System.Func<Task>)(                    async () => await driver.AddDependencyAsync(
                        new DependencyDetail
                        {
                            Commit = "67890",
                            Name = "Foo.Bar",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "1.2.4",
                            Pinned = false,
                            Type = DependencyType.Product
                        })))).Should().ThrowExactlyAsync<DependencyException>();

                await driver.VerifyAsync();
            });
        }

        [Test]
        public async Task AddProductDependency4()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency4), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.3",
                        Type = DependencyType.Product
                    });

                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "67890",
                        Name = "Foo.Bar2",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.4",
                        Pinned = true,
                        Type = DependencyType.Product
                    });

                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "67890",
                        Name = "Foo.Bar3",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.4",
                        Type = DependencyType.Toolset
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add, where the package version isn't in the details file, but is in Versions.props.
        /// This this case, should update Versions.props
        /// </summary>
        [Test]
        public async Task AddProductDependency5()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency5), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "123",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/bop/bop",
                        Version = "1.2.3",
                        Type = DependencyType.Product
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add a basic dependency that has dashes in the name.
        /// </summary>
        [Test]
        public async Task AddProductDependency6()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddProductDependency6), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "dotnet-ef",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "1.2.3",
                        Pinned = true,
                        Type = DependencyType.Product
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update a dependency only existing in Versions.Details.xml
        /// </summary>
        [Test]
        public async Task UpdateDependencies1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateDependencies1), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "4",
                            Name = "Existing.Dependency",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "4.5.6"
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Attempt to update a non-existing dependency
        /// </summary>
        [Test]
        public async Task UpdateDependencies2()
        {
            // Use inputs from previous test.
            await DependencyTestDriver.TestNoCompare(nameof(UpdateDependencies1), async driver =>
            {
                await (((System.Func<Task>)(async () => await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "4",
                            Name = "Foo.Bar",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "4.5.6"
                        }
                    })))).Should().ThrowExactlyAsync<DependencyException>();
            });
        }

        /// <summary>
        /// Update a dependency with new casing.
        /// </summary>
        [Test]
        public async Task UpdateDependencies3()
        {
            // Use inputs from previous test.
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateDependencies3), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "4",
                            Name = "Existing.DEPendency",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "4.5.6"
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update a dependency with new casing and alternate property names
        /// </summary>
        [Test]
        public async Task UpdateDependencies4()
        {
            // Use inputs from previous test.
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateDependencies4), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "4",
                            Name = "Existing.DEPendency",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "4.5.6"
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update dependencies including a pinned dependency which should not be updated
        /// </summary>
        [Test]
        public async Task UpdateDependencies5()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateDependencies5), async driver =>
            {
                await driver.UpdatePinnedDependenciesAsync();
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Support both Version and PackageVersion properties in Versions.props.
        /// When adding, use what's already in the file.
        /// </summary>
        [Test]
        public async Task SupportAlternateVersionPropertyFormats1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(SupportAlternateVersionPropertyFormats1), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "4.5.6",
                        Pinned = true,
                        Type = DependencyType.Product
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Support both Version and PackageVersion properties in Versions.props.
        /// </summary>
        [Test]
        public async Task SupportAlternateVersionPropertyFormats2()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(SupportAlternateVersionPropertyFormats2), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "12345",
                        Name = "Foo.Bar",
                        RepoUri = "https://foo.com/foo/bar",
                        Version = "4.5.6",
                        Pinned = true,
                        Type = DependencyType.Product,
                    });
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "4",
                            Name = "Existing.Dependency",
                            RepoUri = "https://foo.com/foo/bar",
                            Version = "4.5.6",
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add an arcade dependency.
        /// - Does not currently test script download
        /// </summary>
        [Test]
        public async Task AddArcadeDependency1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddArcadeDependency1), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "123",
                        Name = "Microsoft.DotNet.Arcade.Sdk",
                        RepoUri = "https://github.com/dotnet/arcade",
                        Version = "1.0",
                        Pinned = true,
                        Type = DependencyType.Toolset
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Add an arcade dependency.  Not in version.details but in global.json  Should update.
        /// - Does not currently test script download
        /// </summary>
        [Test, Ignore("Not able to update existing version info when adding new dependency. https://github.com/dotnet/arcade/issues/1095")]
        public async Task AddArcadeDependency2()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(AddArcadeDependency1), async driver =>
            {
                await driver.AddDependencyAsync(
                    new DependencyDetail
                    {
                        Commit = "123",
                        Name = "Microsoft.DotNet.Arcade.Sdk",
                        RepoUri = "https://github.com/dotnet/arcade",
                        Version = "2.0",
                        Pinned = false,
                        Type = DependencyType.Toolset
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update the arcade dependency to a new version.
        /// - Does not currently test script download
        /// </summary>
        [Test]
        public async Task UpdateArcadeDependency1()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateArcadeDependency1), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "456",
                            Name = "Microsoft.DotNet.Arcade.Sdk",
                            RepoUri = "https://github.com/dotnet/arcade",
                            Version = "2.0"
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update the arcade dependency to a new version, though it's not in global.json
        /// </summary>
        [Test]
        public async Task UpdateArcadeDependency2()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateArcadeDependency2), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "456",
                            Name = "Microsoft.DotNet.Arcade.Sdk",
                            RepoUri = "https://github.com/dotnet/arcade",
                            Version = "2.0"
                        }
                    });
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        /// Update the arcade dependency to a new version, with an associated global.json update.
        /// </summary>
        [Test]
        public async Task UpdateArcadeDependencyWithSdkUpdate()
        {
            await DependencyTestDriver.TestAndCompareOutput(nameof(UpdateArcadeDependencyWithSdkUpdate), async driver =>
            {
                await driver.UpdateDependenciesAsync(
                    new List<DependencyDetail> {
                        new DependencyDetail
                        {
                            Commit = "456",
                            Name = "Microsoft.DotNet.Arcade.Sdk",
                            RepoUri = "https://github.com/dotnet/arcade",
                            Version = "2.0"
                        }
                    }, new NuGet.Versioning.SemanticVersion(10, 1, 1, "preview-1234"));
                await driver.VerifyAsync();
            });
        }

        /// <summary>
        ///     Sentinel test for checking that the normal version suffix isn't the end
        ///     of the alternate suffix. While other tests will fail if this is the case,
        ///     this makes diagnosing it easier.
        /// </summary>
        [Test]
        public void CheckAlternateSuffix()
        {
            VersionFiles.VersionPropsAlternateVersionElementSuffix.EndsWith(
                         VersionFiles.VersionPropsVersionElementSuffix).Should().BeFalse();
        }
    }
}
