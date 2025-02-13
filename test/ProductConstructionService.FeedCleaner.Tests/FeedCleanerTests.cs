// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using FluentAssertions;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace ProductConstructionService.FeedCleaner.Tests;

[TestFixture, NonParallelizable]
public class FeedCleanerTests
{
    private Mock<IHostEnvironment> _env = new();
    private ServiceProvider? _provider;
    private Dictionary<string, AzureDevOpsFeed> _feeds = [];

    private const string SomeAccount = "someAccount";
    private const string UnmanagedFeedName = "some-other-feed";
    private const string FeedWithAllPackagesReleasedName = "darc-pub-some-repo-12345679";
    private const string FeedWithUnreleasedPackagesName = "darc-int-some-repo-12345678";
    private const string ReleasedPackagePrefix = "packageInNuget";

    private FeedCleanerJob InitializeFeedCleaner(string name)
    {
        var services = new ServiceCollection();
        _env = new Mock<IHostEnvironment>(MockBehavior.Strict);
        _feeds = SetupFeeds(SomeAccount);

        services.AddSingleton(_env.Object);
        services.AddLogging();
        services.AddDbContext<BuildAssetRegistryContext>(
            options =>
            {
                options.UseInMemoryDatabase(name);
            });

        services.Configure<FeedCleanerOptions>(
            (options) =>
            {
                options.Enabled = true;

                options.AzdoAccounts =
                [
                    SomeAccount
                ];
            }
        );

        services.AddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.AddSingleton(SetupAzdoMock().Object);
        services.AddSingleton(SetupHttpClientFactoryMock().Object);
        services.Configure<AzureDevOpsTokenProviderOptions>(
            (options) =>
            {
                options[SomeAccount] = new()
                {
                    Token = "someToken"
                };
            });

        services.AddTransient<FeedCleanerJob>();
        services.AddTransient<FeedCleaner>();

        _provider = services.BuildServiceProvider();
        var scope = _provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        SetupAssetsFromFeeds(context);

        return scope.ServiceProvider.GetRequiredService<FeedCleanerJob>();
    }

    [Test]
    public async Task OnlyDeletesReleasedPackagesFromManagedFeeds()
    {
        var feedCleaner = InitializeFeedCleaner(nameof(OnlyDeletesReleasedPackagesFromManagedFeeds));
        await feedCleaner.CleanManagedFeedsAsync();

        var unreleasedFeed = _feeds[FeedWithUnreleasedPackagesName];
        unreleasedFeed.Packages.Should().HaveCount(2);
        var packagesWithDeletedVersions = unreleasedFeed.Packages.Where(p => p.Versions.Any(v => v.IsDeleted)).ToList();
        packagesWithDeletedVersions.Should().ContainSingle();
        packagesWithDeletedVersions.First().Name.Should().Be($"{ReleasedPackagePrefix}1");
        var deletedVersions = packagesWithDeletedVersions.First().Versions.Where(v => v.IsDeleted).ToList();
        deletedVersions.Should().ContainSingle();
        deletedVersions.First().Version.Should().Be("1.0");

        _feeds[UnmanagedFeedName].Packages.Should().NotContain(p => p.Versions.Any(v => v.IsDeleted));
    }

    [Test]
    public async Task UpdatesAssetLocationsForReleasedPackages()
    {
        var feedCleaner = InitializeFeedCleaner(nameof(UpdatesAssetLocationsForReleasedPackages));
        await feedCleaner.CleanManagedFeedsAsync();

        var context = _provider!.CreateScope().ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        
        var updatedAssets = context.Assets
            .Include(a => a.Locations)
            .Where(a => a.Name.Contains(ReleasedPackagePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        updatedAssets.Should().HaveCount(4);

        // These just updated assets should only have nuget.org as their location
        updatedAssets.Should().NotContain(a =>
            !a.Locations.Any(l => l.Location.Contains(FeedConstants.NuGetOrgLocation)));
        updatedAssets.Where(a => a.Locations.Count > 1).Count().Should().Be(0);

        // "unreleasedPackage1" hasn't been released, should only have the stable feed as its location
        var assetsInRemainingFeed = context.Assets
            .Include(a => a.Locations)
            .Where(a => a.Locations.Any(l => l.Location.Contains(FeedWithUnreleasedPackagesName)))
            .ToList();
        assetsInRemainingFeed.Should().ContainSingle();
        assetsInRemainingFeed.First().Name.Should().Be("unreleasedPackage1");
    }

    private void SetupAssetsFromFeeds(BuildAssetRegistryContext context)
    {
        List<Asset> assets = [];
        foreach ((string feedName, AzureDevOpsFeed feed) in _feeds)
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
                        Locations =
                        [
                            new AssetLocation()
                            {
                                Type = LocationType.NugetFeed,
                                Location = $"https://pkgs.dev.azure.com/{feed.Account}/{projectSection}_packaging/{feedName}/nuget/v3/index.json"
                            }
                        ]
                    });
                }
            }
        }

        context.Assets.AddRange(assets);
        context.SaveChanges();
    }

    private Mock<IAzureDevOpsClient> SetupAzdoMock()
    {
        var azdoClientMock = new Mock<IAzureDevOpsClient>(MockBehavior.Strict);
        azdoClientMock.Setup(a => a.GetFeedsAsync(SomeAccount)).ReturnsAsync(_feeds.Select(kvp => kvp.Value).ToList());
        azdoClientMock.Setup(a => a.GetPackagesForFeedAsync(SomeAccount, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string account, string project, string feed, bool includeDeleted) => Task.FromResult(_feeds[feed].Packages.Where(p => p.Versions.Any(v => !v.IsDeleted)).ToList()));
        azdoClientMock.Setup(a => a.DeleteNuGetPackageVersionFromFeedAsync(SomeAccount, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string, string>((account, project, feed, package, version) => MarkVersionAsDeleted(_feeds[feed].Packages, package, version))
            .Returns(Task.CompletedTask);
        return azdoClientMock;
    }

    private Mock<IHttpClientFactory> SetupHttpClientFactoryMock()
    {
        Mock<HttpMessageHandler> handlerMock = new();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
            {
                var response = new HttpResponseMessage();
                if (request.RequestUri != null &&
                    request.RequestUri.AbsolutePath.Contains(ReleasedPackagePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = HttpStatusCode.OK;
                }
                else
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                }
                return response;
            });

        Mock<IHttpClientFactory> httpClientFactoryMock = new();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handlerMock.Object));

        return httpClientFactoryMock;
    }

    private static void MarkVersionAsDeleted(List<AzureDevOpsPackage> packages, string packageName, string version)
    {
        foreach (AzureDevOpsPackage package in packages.Where(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (AzureDevOpsPackageVersion packageVersion in package.Versions.Where(v => v.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                packageVersion.IsDeleted = true;
            }
        }
    }

    private static Dictionary<string, AzureDevOpsFeed> SetupFeeds(string account)
    {
        var someProject = new AzureDevOpsProject("0", "someProject");
        var allFeeds = new Dictionary<string, AzureDevOpsFeed>();


        var managedFeedWithUnreleasedPackages = new AzureDevOpsFeed(account, "1", FeedWithUnreleasedPackagesName, null)
        {
            Packages =
            [
                new AzureDevOpsPackage("unreleasedPackage1", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                    ]
                },
                new AzureDevOpsPackage($"{ReleasedPackagePrefix}1", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                    ]
                }
            ]
        };
        allFeeds.Add(managedFeedWithUnreleasedPackages.Name, managedFeedWithUnreleasedPackages);

        var managedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "2", FeedWithAllPackagesReleasedName, someProject)
        {
            Packages =
            [
                new AzureDevOpsPackage($"{ReleasedPackagePrefix}2", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                    ]
                },
                new AzureDevOpsPackage($"{ReleasedPackagePrefix}3", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                        new AzureDevOpsPackageVersion("2.0", false)
                    ]
                }
            ]
        };
        allFeeds.Add(managedFeedWithEveryPackageReleased.Name, managedFeedWithEveryPackageReleased);

        // add a feed with all released packages, but that doesn't match the name pattern. It shouldn't be touched by the cleaner.
        var nonManagedFeedWithEveryPackageReleased = new AzureDevOpsFeed(account, "3", UnmanagedFeedName, someProject)
        {
            Packages =
            [
                new AzureDevOpsPackage("Newtonsoft.Json", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("12.0.2", isDeleted: false)
                    ]
                },
                new AzureDevOpsPackage("releasedPackage1", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false)
                    ]
                },
                new AzureDevOpsPackage("releasedPackage2", "nuget")
                {
                    Versions =
                    [
                        new AzureDevOpsPackageVersion("1.0", isDeleted: false),
                        new AzureDevOpsPackageVersion("2.0", isDeleted: false)
                    ]
                }
            ]
        };
        allFeeds.Add(nonManagedFeedWithEveryPackageReleased.Name, nonManagedFeedWithEveryPackageReleased);

        return allFeeds;
    }
}
