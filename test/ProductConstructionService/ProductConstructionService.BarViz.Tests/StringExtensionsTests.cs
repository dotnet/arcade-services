// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class StringExtensionsTests
{
    [Test]
    public void SplitIntoTextAndUrls_NullOrEmpty_ReturnsEmpty()
    {
        StringExtensions.SplitIntoTextAndUrls(null).Should().BeEmpty();
        StringExtensions.SplitIntoTextAndUrls(string.Empty).Should().BeEmpty();
    }

    [Test]
    public void SplitIntoTextAndUrls_PlainText_ReturnsSingleTextSegment()
    {
        var segments = StringExtensions.SplitIntoTextAndUrls("no links here");

        segments.Should().ContainSingle();
        segments[0].IsUrl.Should().BeFalse();
        segments[0].Text.Should().Be("no links here");
    }

    [Test]
    public void SplitIntoTextAndUrls_UrlInMiddle_SplitsAroundUrl()
    {
        var segments = StringExtensions.SplitIntoTextAndUrls("See https://github.com/dotnet/arcade for details");

        segments.Should().HaveCount(3);
        segments[0].Should().Be(new StringExtensions.TextSegment("See ", false));
        segments[1].Should().Be(new StringExtensions.TextSegment("https://github.com/dotnet/arcade", true));
        segments[2].Should().Be(new StringExtensions.TextSegment(" for details", false));
    }

    [Test]
    public void SplitIntoTextAndUrls_TrailingPunctuation_ExcludedFromUrl()
    {
        var segments = StringExtensions.SplitIntoTextAndUrls("Open https://example.com/path).");

        segments.Should().HaveCount(3);
        segments[0].Should().Be(new StringExtensions.TextSegment("Open ", false));
        segments[1].Should().Be(new StringExtensions.TextSegment("https://example.com/path", true));
        segments[2].Should().Be(new StringExtensions.TextSegment(").", false));
    }

    [Test]
    public void SplitIntoTextAndUrls_MultipleUrls_AllDetected()
    {
        var segments = StringExtensions.SplitIntoTextAndUrls("a https://a.com b http://b.com");

        segments.Should().HaveCount(4);
        segments[0].Should().Be(new StringExtensions.TextSegment("a ", false));
        segments[1].Should().Be(new StringExtensions.TextSegment("https://a.com", true));
        segments[2].Should().Be(new StringExtensions.TextSegment(" b ", false));
        segments[3].Should().Be(new StringExtensions.TextSegment("http://b.com", true));
    }

    [Test]
    public void SplitIntoTextAndUrls_UrlOnly_ReturnsSingleUrlSegment()
    {
        var segments = StringExtensions.SplitIntoTextAndUrls("https://example.com");

        segments.Should().ContainSingle();
        segments[0].Should().Be(new StringExtensions.TextSegment("https://example.com", true));
    }
}
