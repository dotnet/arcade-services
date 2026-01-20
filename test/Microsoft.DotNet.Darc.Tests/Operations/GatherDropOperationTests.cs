// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using System.Reflection;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GatherDropOperationTests
{
    // IsBlobFeedUrl is a private static method, so we need to use reflection to test it
    private static bool InvokeIsBlobFeedUrl(string location)
    {
        var type = typeof(Microsoft.DotNet.Darc.Operations.GatherDropOperation);
        var method = type.GetMethod("IsBlobFeedUrl", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("IsBlobFeedUrl method should exist");
        return (bool)method.Invoke(null, [location]);
    }

    [TestCase("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json", ExpectedResult = true)]
    [TestCase("https://dotnetcli.blob.core.windows.net/dotnet/index.json", ExpectedResult = true)]
    [TestCase("https://dotnetclichecksums.blob.core.windows.net/dotnet/index.json", ExpectedResult = true)]
    public bool IsBlobFeedUrl_RecognizesBlobCoreWindowsNet(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }

    [TestCase("https://ci.dot.net/public", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/internal", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/public/", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/internal/", ExpectedResult = true)]
    [TestCase("https://CI.DOT.NET/public", ExpectedResult = true)]
    [TestCase("https://Ci.Dot.Net/internal", ExpectedResult = true)]
    public bool IsBlobFeedUrl_RecognizesCiDotNet(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }

    [TestCase("https://pkgs.dev.azure.com/dnceng/public/_packaging/public-feed-name/nuget/v3/index.json", ExpectedResult = false)]
    [TestCase("https://pkgs.dev.azure.com/dnceng/_packaging/internal-feed-name/nuget/v3/index.json", ExpectedResult = false)]
    [TestCase("https://www.nuget.org/api/v2", ExpectedResult = false)]
    [TestCase("https://example.com/some-feed", ExpectedResult = false)]
    [TestCase("not-a-valid-uri", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    public bool IsBlobFeedUrl_DoesNotRecognizeNonBlobUrls(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }
}
