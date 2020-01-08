// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.AzureDevOps;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FeedCleaner.Tests
{
    public class FeedCleanerTests : IDisposable
    {
        private readonly Lazy<BuildAssetRegistryContext> _context;
        private readonly Mock<IHostingEnvironment> Env;
        private readonly ServiceProvider Provider;
        private readonly IServiceScope Scope;

        private static readonly string someAccount = "someAccount";
        
        public FeedCleanerTests()
        {
            var services = new ServiceCollection();
            Env = new Mock<IHostingEnvironment>(MockBehavior.Strict);
            services.AddSingleton(Env.Object);
            services.AddLogging();
            services.AddDbContext<BuildAssetRegistryContext>(
                options => { options.UseInMemoryDatabase("BuildAssetRegistry"); });
            services.Configure<FeedCleanerOptions>(
                (options) =>
                {
                    options.Enabled = true;
                    options.ReleasePackageFeeds = new List<(string account, string project, string name)>()
                    {
                        (someAccount, "someProject", "releaseFeed"),
                    };

                    options.AzdoAccounts = new List<string>()
                    {
                        someAccount
                    };
                }
            );
            services.AddAzureDevOpsTokenProvider();
            services.Configure<AzureDevOpsTokenProviderOptions>(
                (options) =>
                {
                    options.Tokens.Add(someAccount, "someToken");
                });

            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            _context = new Lazy<BuildAssetRegistryContext>(GetContext);
        }

        public void Dispose()
        {
            Env.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }
        private BuildAssetRegistryContext GetContext()
        {
            return Scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        }

        [Fact]
        public async void CleanFeedsAsyncTests()
        {
            var cleaner = ActivatorUtilities.CreateInstance<FeedCleaner>(Scope.ServiceProvider);
            var feeds = SetupFeeds(someAccount);

            Mock<IAzureDevOpsClient> azdoClientMock = new Mock<IAzureDevOpsClient>(MockBehavior.Strict);
            azdoClientMock.Setup(a => a.GetFeedsAsync(someAccount)).Returns(Task.FromResult(feeds.Select(kvp => kvp.Value).ToList()));
            azdoClientMock.Setup(a => a.GetPackagesForFeedAsync(someAccount, It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string account, string project, string feed) => Task.FromResult(feeds[feed].Packages));
            azdoClientMock.Setup(a => a.DeleteNuGetPackageVersionFromFeedAsync(someAccount, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string, string, string>((account, project, feed, package, version) => MarkVersionAsDeleted(feeds[feed].Packages, package, version))
                .Returns(Task.CompletedTask);
            azdoClientMock.Setup(a => a.DeleteFeedAsync(someAccount, It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((account, project, feedIdentifier) => feeds.Remove(feedIdentifier))
                .Returns(Task.CompletedTask);

            cleaner.AzureDevOpsClients = new Dictionary<string, IAzureDevOpsClient>
            {
                { someAccount, azdoClientMock.Object }
            };

            SetupAssetsFromFeeds(feeds);
            var x = await _context.Value.Assets.ToListAsync();

            await cleaner.CleanManagedFeedsAsync();

            // A few things should've happened after cleaning:
            // - Assets in the two test feeds that are also present in the release feed should have the release feed in their locations in the DB.
            // - The darc-pub-some-repo-12345679 feed should be deleted, as all its packages are either in the release feed or in nuget.org
            // - The darc-int-some-repo-12345678 feed should still be there, and the releasedPackage1 feed should have version 1.0 deleted
            // - The some-other-feed feed should still exist, with all three packages and no versions deleted.

            Assert.Equal(3, feeds.Count);
            Assert.True(feeds.ContainsKey("releaseFeed"));
            Assert.True(feeds.ContainsKey("some-other-feed"));
            Assert.True(feeds.ContainsKey("darc-int-some-repo-12345678"));
            Assert.False(feeds.ContainsKey("darc-pub-some-repo-12345679"));
            Assert.DoesNotContain(feeds["some-other-feed"].Packages, p => p.Versions.Any(v => v.IsDeleted));

            // Check the assets for the feed where all assets were released
            var assetsInDeletedFeed = GetContext().Assets.Where(a => a.Locations.Any(l => l.Location.Contains("darc-pub-some-repo-12345679"))).ToList();
            Assert.Equal(4, assetsInDeletedFeed.Count);
            Assert.Contains(assetsInDeletedFeed,
                a => a.Name.Equals("Newtonsoft.Json") &&
                a.Version == "12.0.2" &&
                a.Locations.Any(l => l.Location.Equals("https://api.nuget.org/v3/index.json")));

            // All other assets should also have been updated to be in the release feed
            Assert.DoesNotContain(assetsInDeletedFeed,
                a => !a.Name.Equals("Newtonsoft.Json") &&
                !a.Locations.Any(l => l.Location.Contains("releaseFeed")));

            // Check the assets for the feed where not all packages were released, one package should 
            var assetsInRemainingFeed = GetContext().Assets.Where(a => a.Locations.Any(l => l.Location.Contains("darc-int-some-repo-12345678"))).ToList();
            Assert.Equal(2, assetsInRemainingFeed.Count);
            var releasedAssets = assetsInRemainingFeed.Where(a => a.Locations.Any(l => l.Location.Contains("releaseFeed"))).ToList();
            Assert.Single(releasedAssets);
            Assert.Equal("releasedPackage1", releasedAssets.FirstOrDefault().Name);
            Assert.Equal("1.0", releasedAssets.FirstOrDefault().Version);
            var unreleasedAssets = assetsInRemainingFeed.Where(a => a.Locations.All(l => l.Location.Contains("darc-int-some-repo-12345678"))).ToList();
            Assert.Single(unreleasedAssets);
            Assert.Equal("unreleasedPackage1", unreleasedAssets.FirstOrDefault().Name);
            Assert.Equal("1.0", unreleasedAssets.FirstOrDefault().Version);

            // Now check that the released versions have been deleted from the stable feed
            var unreleasedFeed = feeds["darc-int-some-repo-12345678"];
            Assert.Equal(2, unreleasedFeed.Packages.Count);
            var packagesWithDeletedVersions = unreleasedFeed.Packages.Where(p => p.Versions.Any(v => v.IsDeleted)).ToList();
            Assert.Single(packagesWithDeletedVersions);
            Assert.Equal("releasedPackage1", packagesWithDeletedVersions.FirstOrDefault().Name);
            var deletedVersions = packagesWithDeletedVersions.FirstOrDefault().Versions.Where(v => v.IsDeleted);
            Assert.Single(deletedVersions);
            Assert.Equal("1.0", deletedVersions.FirstOrDefault().Version);
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

        private void SetupAssetsFromFeeds(Dictionary<string, AzureDevOpsFeed> feeds)
        {
            List<Asset> assets = new List<Asset>();
            foreach ((string feedName, AzureDevOpsFeed feed) in feeds)
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

            var context = GetContext();
            context.Assets.AddRange(assets);
            context.SaveChangesAsync();
        }

        private Dictionary<string, AzureDevOpsFeed> SetupFeeds(string account)
        {
            
            AzureDevOpsProject someProject = new AzureDevOpsProject("0", "someProject");
            var allFeeds = new Dictionary<string, AzureDevOpsFeed>();

            // This is the reference release feed.
            var releaseFeed = new AzureDevOpsFeed(account, "0", "releaseFeed", someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false),
                            new AzureDevOpsPackageVersion("2.0", true),
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false),
                            new AzureDevOpsPackageVersion("2.0", false),
                        }
                    }
                }
            };
            allFeeds.Add("releaseFeed", releaseFeed);

            var managedFeedWithUnreleasedPackages = new AzureDevOpsFeed(account, "1", "darc-int-some-repo-12345678", null)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("unreleasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false),
                        }
                    }
                }
            };
            allFeeds.Add("darc-int-some-repo-12345678", managedFeedWithUnreleasedPackages);

            var managedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "2", "darc-pub-some-repo-12345679", someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("Newtonsoft.Json", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("12.0.2", false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false),
                            new AzureDevOpsPackageVersion("2.0", false)
                        }
                    }
                }
            };
            allFeeds.Add("darc-pub-some-repo-12345679", managedFeedWithEveryPackageReleased);


            // add a feed with all released packages, but that doesn't match the name pattern. It shouldn't be touched by the cleaner.
            var nonManagedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "3", "some-other-feed", someProject)
            {
                Packages = new List<AzureDevOpsPackage>()
                {
                    new AzureDevOpsPackage("Newtonsoft.Json", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("12.0.2", false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage1", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false)
                        }
                    },
                    new AzureDevOpsPackage("releasedPackage2", "nuget")
                    {
                        Versions = new AzureDevOpsPackageVersion[]
                        {
                            new AzureDevOpsPackageVersion("1.0", false),
                            new AzureDevOpsPackageVersion("2.0", false)
                        }
                    }
                }
            };
            allFeeds.Add("some-other-feed", nonManagedFeedWithEveryPackageReleased);

            return allFeeds;
        }
    }
}
