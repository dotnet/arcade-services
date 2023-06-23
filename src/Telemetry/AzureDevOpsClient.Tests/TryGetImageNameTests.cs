using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Microsoft.DotNet.Internal.AzureDevOps.Tests;

[TestFixture]
public class TryGetImageNameTests
{
    public static string EmptyUrl = "https://www.fakeurl.test/";

    private ILogger<AzureDevOpsClient> _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AzureDevOpsClient>();

    [Test]
    [TestCase(new string[] { "a", "b", "b", "c"}, new string[] { "([ab])", "([ab])", "(c)" }, "c")]
    [TestCase(new string[] { "a", "b", "b", "b", "b", "c" }, new string[] { "([ab])", "([ab])", "(c)" }, "c")]
    [TestCase(new string[] { "a", "c", "c" }, new string[] { "([ab])", "([ab])", "(c)" }, null)]
    public async Task AzureDevOpsClientShouldMatchLogLines(string[] lines, string[] regexStrings, string? expectedResult)
    {
        var mockHttpClientFactory = new MockHttpClientFactory();
        var response = string.Join(Environment.NewLine, lines);
        mockHttpClientFactory.AddCannedResponse(EmptyUrl, response);
        var client = new AzureDevOpsClient(new AzureDevOpsClientOptions(), _logger, mockHttpClientFactory);
        var regexes = regexStrings.Select(regex => new Regex(regex)).ToList();

        var result = await client.MatchLogLineSequence(EmptyUrl, regexes, CancellationToken.None);

        result.Should().Be(expectedResult);
    }
}
