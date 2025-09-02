// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

[TestFixture]
[Category("auto-generated")]
public class HttpRequestManagerTests
{
    /// <summary>
    /// Validates that the constructor accepts a variety of valid inputs without throwing,
    /// including different HTTP methods, URIs (normal/empty/whitespace/long/special), body content variations,
    /// logging flag, authentication headers, an optional request configuration delegate, and enum values.
    /// Inputs vary by test cases provided via source.
    /// Expected: An instance is created successfully (no exception) and is non-null.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Constructor_ValidInput_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_VariedInputs_DoesNotThrow(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        ILogger logger,
        string body,
        string versionOverride,
        bool logFailure,
        AuthenticationHeaderValue authHeader,
        Action<HttpRequestMessage> configureRequestMessage,
        HttpCompletionOption httpCompletionOption)
    {
        // Arrange
        // Inputs provided by TestCaseSource.

        // Act
        var instance = new HttpRequestManager(
            client,
            method,
            requestUri,
            logger,
            body,
            versionOverride,
            logFailure,
            authHeader,
            configureRequestMessage,
            httpCompletionOption);

        // Assert
        instance.Should().NotBeNull();
    }

    private static System.Collections.IEnumerable Constructor_ValidInput_Cases()
    {
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var logger = loggerMock.Object;

        yield return new TestCaseData(
            new HttpClient(),
            HttpMethod.Get,
            "https://example.com/api/resource",
            logger,
            null,                          // body
            null,                          // versionOverride
            true,                          // logFailure
            null,                          // authHeader
            null,                          // configureRequestMessage
            HttpCompletionOption.ResponseContentRead
        ).SetName("Typical_GET_MinimalOptions_NoThrow");

        yield return new TestCaseData(
            new HttpClient(),
            new HttpMethod("CUSTOM"),
            string.Empty,                  // empty URI
            logger,
            string.Empty,                  // empty body
            "1.0.0",
            false,                         // logFailure
            new AuthenticationHeaderValue("Bearer", "token"),
            new Action<HttpRequestMessage>(_ => { /* no-op */ }),
            HttpCompletionOption.ResponseHeadersRead
        ).SetName("CustomMethod_EmptyUri_AuthHeader_HeadersRead_NoThrow");

        yield return new TestCaseData(
            new HttpClient(),
            HttpMethod.Post,
            "   ",                         // whitespace URI
            logger,
            "   ",                         // whitespace body
            "version-x",
            true,
            new AuthenticationHeaderValue("Basic", "abcd"),
            new Action<HttpRequestMessage>(_ => { /* no-op */ }),
            (HttpCompletionOption)999      // out-of-range enum
        ).SetName("Post_WhitespaceUri_LongVersion_InvalidEnum_NoThrow");

        yield return new TestCaseData(
            new HttpClient(),
            HttpMethod.Put,
            new string('a', 2048),         // very long URI string (not used/executed)
            logger,
            new string('b', 4096),         // very long body
            new string('v', 100),
            true,
            null,
            null,
            HttpCompletionOption.ResponseContentRead
        ).SetName("Put_VeryLongUriAndBody_NoThrow");

        yield return new TestCaseData(
            new HttpClient(),
            HttpMethod.Delete,
            "https://exa mple.com/pa th?query=va lue&sp%c",
            logger,
            "{ \"key\": \"val\\u0001\\u0002\\t\\n\" }", // body with control/special chars
            "v2",
            false,
            null,
            new Action<HttpRequestMessage>(m => m.Headers.Add("X-Test", "1")),
            HttpCompletionOption.ResponseHeadersRead
        ).SetName("Delete_SpecialCharsInUriAndBody_WithConfigurer_NoThrow");

        yield return new TestCaseData(
            new HttpClient(),
            new HttpMethod("PATCH"),
            "/relative/path",
            logger,
            "{\"name\":\"value\"}",
            "",
            true,
            new AuthenticationHeaderValue("Bearer", "another-token"),
            null,
            HttpCompletionOption.ResponseContentRead
        ).SetName("Patch_RelativeUri_WithBody_WithAuth_NoThrow");
    }
}
