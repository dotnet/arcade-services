// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class AuthenticationConfigurationTests
{
    [TestCase("/")]
    [TestCase("/Account/SignIn")]
    [TestCase("/some/path")]
    [TestCase("/some/path?foo=bar")]
    [TestCase("/path/with/percent-encoded/%5Cevil.example/x")]
    public void IsLocalReturnUrl_AcceptsLocalPaths(string url)
    {
        AuthenticationConfiguration.IsLocalReturnUrl(url).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("//evil.example/phish")]
    [TestCase("///evil.example/phish")]
    [TestCase(@"/\evil.example/phish")]
    [TestCase(@"/\\evil.example/phish")]
    [TestCase("http://evil.example/phish")]
    [TestCase("https://evil.example/phish")]
    [TestCase("evil.example/phish")]
    [TestCase("javascript:alert(1)")]
    public void IsLocalReturnUrl_RejectsOffOriginUrls(string? url)
    {
        AuthenticationConfiguration.IsLocalReturnUrl(url).Should().BeFalse();
    }
}
