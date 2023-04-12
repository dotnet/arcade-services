using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps.Tests;

[TestFixture]
public class TryGetImageNameTests
{
    public static string EmptyUrl = "https://www.fakeurl.test/";

    private ILogger<AzureDevOpsClient> _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AzureDevOpsClient>();

    [Test]
    public async Task AzureDevOpsClientShouldReturnImageName()
    {
        var mockHttpClientFactory = new MockHttpClientFactory();
        var response = """
            a
            b
            b
            c
            """;
        var regexes = new Regex[]
        {
            new Regex("([ab])"),
            new Regex("([ab])"),
            new Regex("(c)")
        };
        mockHttpClientFactory.AddCannedResponse(EmptyUrl, response);
        var client = new AzureDevOpsClient(new AzureDevOpsClientOptions(), _logger, mockHttpClientFactory);

        var result = await client.TryGetImageName(EmptyUrl, regexes, CancellationToken.None);

        result.Should().Be("c");
    }

    [Test]
    public async Task AzureDevOpsClientShouldNotReturnImageName()
    {
        var mockHttpClientFactory = new MockHttpClientFactory();
        var response = """
            a
            b
            b
            b
            c
            """;
        var regexes = new Regex[]
        {
            new Regex("([ab])"),
            new Regex("([ab])"),
            new Regex("(c)")
        };
        mockHttpClientFactory.AddCannedResponse(EmptyUrl, response);
        var client = new AzureDevOpsClient(new AzureDevOpsClientOptions(), _logger, mockHttpClientFactory);

        var result = await client.TryGetImageName(EmptyUrl, regexes, CancellationToken.None);

        result.Should().BeNull();
    }
}
