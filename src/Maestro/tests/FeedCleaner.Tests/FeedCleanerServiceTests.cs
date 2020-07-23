// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.AzureDevOps;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;

namespace FeedCleanerService.Tests
{
    [TestFixture, NonParallelizable]
    public class FeedCleanerServiceTests : IDisposable
    {
        private Lazy<BuildAssetRegistryContext> _context;
        private Mock<IHostEnvironment> Env;
        private ServiceProvider Provider;
        private IServiceScope Scope;
        private Dictionary<string, AzureDevOpsFeed> Feeds;
        private Mock<IAzureDevOpsClient> AzdoMock;

        private readonly string SomeAccount = "someAccount";
        private readonly string UnmanagedFeedName = "some-other-feed";
        private readonly string ReleaseFeedName = "release-feed";
        private readonly string FeedWithAllPackagesReleasedName = "darc-pub-some-repo-12345679";
        private readonly string FeedWithUnreleasedPackagesName = "darc-int-some-repo-12345678";

        [SetUp]
        public void FeedCleanerServiceTests_SetUp()
        {
            var services = new ServiceCollection();
            Env = new Mock<IHostEnvironment>(MockBehavior.Strict);
            services.AddSingleton(Env.Object);
            services.AddLogging();
            services.AddDbContext<BuildAssetRegistryContext>(
                options =>
                {
                    options.UseInMemoryDatabase("BuildAssetRegistry");
                    options.EnableServiceProviderCaching(false);
                });
            services.Configure<FeedCleanerOptions>(
                (options) =>
                {
                    options.Enabled = true;
                    options.ReleasePackageFeeds = new List<(string account, string project, string name)>()
                    {
                        (SomeAccount, "someProject", ReleaseFeedName),
                    };

                    options.AzdoAccounts = new List<string>()
                    {
                        SomeAccount
                    };
                }
            );
            services.AddAzureDevOpsTokenProvider();
            services.Configure<AzureDevOpsTokenProviderOptions>(
                (options) =>
                {
                    options.Tokens.Add(SomeAccount, "someToken");
                });

            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            _context = new Lazy<BuildAssetRegistryContext>(GetContext);

            Feeds = SetupFeeds(SomeAccount);
            AzdoMock = SetupAzdoMock();
            SetupAssetsFromFeeds();
        }

        [TearDown]
        public void Dispose()
        {
            Env.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }

        [Test]
        public async Task OnlyDeletesReleasedPackagesFromManagedFeeds()
        {
            FeedCleanerService cleaner = ConfigureFeedCleaner();
            await cleaner.CleanManagedFeedsAsync();
            var unreleasedFeed = Feeds[FeedWithUnreleasedPackagesName];
            unreleasedFeed.Packages.Should().HaveCount(2);
            var packagesWithDeletedVersions = unreleasedFeed.Packages.Where(p => p.Versions.Any(v => v.IsDeleted)).ToList();
            packagesWithDeletedVersions.Should().ContainSingle();
            packagesWithDeletedVersions.First().Name.Should().Be("releasedPackage1");
            var deletedVersions = packagesWithDeletedVersions.First().Versions.Where(v => v.IsDeleted).ToList();
            deletedVersions.Should().ContainSingle();
            deletedVersions.First().Version.Should().Be("1.0");

            Feeds[UnmanagedFeedName].Packages.Should().NotContain(p => p.Versions.Any(v => v.IsDeleted));
        }

        [Test]
        public async Task UpdatesAssetLocationsForReleasedPackages()
        {
            FeedCleanerService cleaner = ConfigureFeedCleaner();
            await cleaner.CleanManagedFeedsAsync();

            // Check the assets for the feed where all packages were released.
            var assetsInDeletedFeed = _context.Value.Assets.Where(a => a.Locations.Any(l => l.Location.Contains(FeedWithAllPackagesReleasedName))).ToList();
            assetsInDeletedFeed.Should().HaveCount(4);
            assetsInDeletedFeed.Should().Contain(                a => a.Name.Equals("Newtonsoft.Json") &&
                a.Version == "12.0.2" &&
                a.Locations.Any(l => l.Location.Equals("https://api.nuget.org/v3/index.json")));

            // All other assets should also have been updated to be in the release feed
            assetsInDeletedFeed.Should().NotContain(                a => !a.Name.Equals("Newtonsoft.Json") &&
                !a.Locations.Any(l => l.Location.Contains(ReleaseFeedName)));

            // "releasedPackage1" should've been released and have its location updated to the released feed.
            var assetsInRemainingFeed = _context.Value.Assets.Where(a => a.Locations.Any(l => l.Location.Contains(FeedWithUnreleasedPackagesName))).ToList();
            assetsInRemainingFeed.Should().HaveCount(2);
            var releasedAssets = assetsInRemainingFeed.Where(a => a.Locations.Any(l => l.Location.Contains(ReleaseFeedName))).ToList();
            releasedAssets.Should().ContainSingle();
            releasedAssets.First().Name.Should().Be("releasedPackage1");
            releasedAssets.First().Version.Should().Be("1.0");

            // "unreleasedPackage1" hasn't been released, should only have the stable feed as its location
            var unreleasedAssets = assetsInRemainingFeed.Where(a => a.Locations.All(l => l.Location.Contains(FeedWithUnreleasedPackagesName))).ToList();
            unreleasedAssets.Should().ContainSingle();
            unreleasedAssets.FirstOrDefault().Name.Should().Be("unreleasedPackage1");
            unreleasedAssets.FirstOrDefault().Version.Should().Be("1.0");
        }

        private BuildAssetRegistryContext GetContext()
        {
            return Scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        }

        private Mock<IAzureDevOpsClient> SetupAzdoMock()
        {
            Mock<IAzureDevOpsClient> azdoClientMock = new Mock<IAzureDevOpsClient>(MockBehavior.Strict);
            azdoClientMock.Setup(a => a.GetFeedsAsync(SomeAccount)).Returns(Task.FromResult(Feeds.Select(kvp => kvp.Value).ToList()));
            azdoClientMock.Setup(a => a.GetPackagesForFeedAsync(SomeAccount, It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string account, string project, string feed) => Task.FromResult(Feeds[feed].Packages));
            azdoClientMock.Setup(a => a.DeleteNuGetPackageVersionFromFeedAsync(SomeAccount, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string, string, string>((account, project, feed, package, version) => MarkVersionAsDeleted(Feeds[feed].Packages, package, version))
                .Returns(Task.CompletedTask);
            azdoClientMock.Setup(a => a.DeleteFeedAsync(SomeAccount, It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((account, project, feedIdentifier) => Feeds.Remove(feedIdentifier))
                .Returns(Task.CompletedTask);
            return azdoClientMock;
        }

        private FeedCleanerService ConfigureFeedCleaner()
        {
            FeedCleanerService cleaner = ActivatorUtilities.CreateInstance<FeedCleanerService>(Scope.ServiceProvider);
            cleaner.AzureDevOpsClients = new Dictionary<string, IAzureDevOpsClient>
            {
                { SomeAccount, AzdoMock.Object }
            };
            return cleaner;
        }

        private void MarkVersionAsDeleted(List<AzureDevOpsPackage> packages, string packageName, string version)
        {
            foreach (var package in packages)
            {
                if (package.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var packageVersion in package.Versions)
                    {
                        if (packageVersion.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
                        {
                            packageVersion.IsDeleted = true;
                        }
                    }
                }
            }
        }

        private void SetupAssetsFromFeeds()
        {
            List<Asset> assets = new List<Asset>();
            foreach ((string feedName, AzureDevOpsFeed feed) in Feeds)
            {
                string projectSection = string.IsNullOrEmpty(feed.Project?.Name) ? "" : $"{feed.Project.Name}/";
                foreach (var package in feed.Packages)
                {
                    foreach (var version in package.Versions)
                    {
                        assets.Add(new Asset()
                        {
                            Name = package.Name,
                            BuildId = 0,
                            NonShipping = false,
                            Version = version.Version,
                            Locations = new List<AssetLocation>()
                            {
                                new AssetLocation()
                                {
                                    Type = LocationType.NugetFeed,
                                    Location = $"https://pkgs.dev.azure.com/{feed.Account}/{projectSection}_packaging/{feedName}/nuget/v3/index.json"
                                }
                            }
                        });
                    }

                }
            }

            var context = _context.Value;
            context.Assets.AddRange(assets);
            context.SaveChanges();
        }

        private Dictionary<string, AzureDevOpsFeed> SetupFeeds(string account)
        {
            AzureDevOpsProject someProject = new AzureDevOpsProject("0", "someProject");
            var allFeeds = new Dictionary<string, AzureDevOpsFeed>();

            // This is the reference release feed.
            var releaseFeed = new AzureDevOpsFeed(account, "0", ReleaseFeedName, someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                            new AzureDevOpsPackageVersion("2.0", isDeleted: true),
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                            new AzureDevOpsPackageVersion("2.0", isDeleted: false),
                        }
                    }
                }
            };
            allFeeds.Add(releaseFeed.Name, releaseFeed);

            var managedFeedWithUnreleasedPackages = new AzureDevOpsFeed(account, "1", FeedWithUnreleasedPackagesName, null)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("unreleasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                        }
                    }
                }
            };
            allFeeds.Add(managedFeedWithUnreleasedPackages.Name, managedFeedWithUnreleasedPackages);

            var managedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "2", FeedWithAllPackagesReleasedName, someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("Newtonsoft.Json", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("12.0.2", isDeleted: false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                            new AzureDevOpsPackageVersion("2.0", false)
                        }
                    }
                }
            };
            allFeeds.Add(managedFeedWithEveryPackageReleased.Name, managedFeedWithEveryPackageReleased);

            // add a feed with all released packages, but that doesn't match the name pattern. It shouldn't be touched by the cleaner.
            var nonManagedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "3", UnmanagedFeedName, someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("Newtonsoft.Json", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("12.0.2", isDeleted: false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                            new AzureDevOpsPackageVersion("2.0", isDeleted: false)
                        }
                    }
                }
            };
            allFeeds.Add(nonManagedFeedWithEveryPackageReleased.Name, nonManagedFeedWithEveryPackageReleased);

            return allFeeds;
        }
    }
}
