// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using AwesomeAssertions;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class PullRequestUrlConversionTests
{
    private static MethodInfo? _turnApiUrlToWebsiteMethod;

    [SetUp]
    public void Setup()
    {
        // Use reflection to get the private static method TurnApiUrlToWebsite
        _turnApiUrlToWebsiteMethod = typeof(PullRequestController).GetMethod("TurnApiUrlToWebsite", BindingFlags.NonPublic | BindingFlags.Static);
        _turnApiUrlToWebsiteMethod.Should().NotBeNull("TurnApiUrlToWebsite method should exist");
    }

    /// <summary>
    /// Helper method to invoke TurnApiUrlToWebsite with named parameters for clarity.
    /// </summary>
    /// <param name="url">The PR URL to convert</param>
    /// <param name="orgName">Optional organization name (used for Azure DevOps)</param>
    /// <param name="repoName">Optional repository name (used for Azure DevOps)</param>
    private static string? InvokeTurnApiUrlToWebsite(string url, string? orgName = null, string? repoName = null)
    {
        return _turnApiUrlToWebsiteMethod!.Invoke(null, [url, orgName, repoName]) as string;
    }

    [TestCase("https://api.github.com/repos/dotnet/dotnet/pulls/3205", "https://github.com/dotnet/dotnet/pull/3205")]
    [TestCase("https://api.github.com/repos/dotnet/runtime/pulls/12345", "https://github.com/dotnet/runtime/pull/12345")]
    [TestCase("https://api.github.com/repos/microsoft/CsWinRT/pulls/999", "https://github.com/microsoft/CsWinRT/pull/999")]
    public void TurnApiUrlToWebsite_ConvertsGitHubApiUrlsToWebUrls(string apiUrl, string expectedWebUrl)
    {
        var result = InvokeTurnApiUrlToWebsite(apiUrl);
        result.Should().Be(expectedWebUrl);
    }

    [TestCase("https://dev.azure.com/dnceng/7ea9116e-9fac-403d-b258-b31fcf1bb293/_apis/git/repositories/test-repo-guid/pullRequests/123", 
              "https://dev.azure.com/dnceng/internal/_git/test-repo-guid/pullrequest/123")]
    public void TurnApiUrlToWebsite_ConvertsAzureDevOpsApiUrlsToWebUrls(string apiUrl, string expectedWebUrl)
    {
        var result = InvokeTurnApiUrlToWebsite(apiUrl);
        result.Should().Be(expectedWebUrl);
    }

    [TestCase("https://github.com/dotnet/dotnet/pull/3205")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-wpf/pullrequest/123")]
    [TestCase("not-a-url")]
    public void TurnApiUrlToWebsite_ReturnsOriginalUrlWhenNotApiUrl(string url)
    {
        var result = InvokeTurnApiUrlToWebsite(url);
        result.Should().Be(url);
    }
}
