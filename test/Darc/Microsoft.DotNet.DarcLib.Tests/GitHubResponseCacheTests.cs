// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Octokit;
using Octokit.Caching;
using Octokit.Internal;
using CachedEntry = Microsoft.DotNet.DarcLib.GitHubResponseCache.GitHubCachedResponse;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class GitHubResponseCacheTests
{
    private const string PullRequestPath = "repos/dotnet/runtime/pulls/123";
    private const string FirstEtag = "W/\"first\"";
    private const string SecondEtag = "\"second\"";
    private const string OpenBody = "{\"state\":\"open\"}";
    private const string ClosedBody = "{\"state\":\"closed\"}";

    private SerializedCache _cache;
    private Mock<IHttpClient> _transport;
    private Queue<IResponse> _responses;
    private List<Dictionary<string, string>> _sentHeaders;
    private Connection _connection;

    [SetUp]
    public void SetUp()
    {
        _cache = new SerializedCache();
        _responses = new Queue<IResponse>();
        _sentHeaders = [];
        _transport = new Mock<IHttpClient>(MockBehavior.Strict);
        _transport.Setup(client => client.Send(
                It.IsAny<IRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Func<object, object>>()))
            .Returns((IRequest request, CancellationToken _, Func<object, object> _) =>
            {
                _sentHeaders.Add(new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase));
                return Task.FromResult(_responses.Dequeue());
            });
        _connection = CreateConnection();
    }

    [TestCase(PullRequestPath)]
    [TestCase(PullRequestPath + "/commits")]
    [TestCase("repos/dotnet/runtime/commits/abc123/status")]
    [TestCase("repos/dotnet/runtime/commits/abc123/check-runs")]
    public async Task AllowedEndpointsRevalidateAndReturnCachedBodyAsync(string path)
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));

        var first = await GetAsync(path);
        var second = await GetAsync(path);

        first.Body["state"].Should().Be("open");
        second.Body["state"].Should().Be("open");
        _sentHeaders[0].Should().NotContainKey("If-None-Match");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders.Should().HaveCount(2);
    }

    [Test]
    public async Task ChangedResponseReplacesBodyAndEtagAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));

        await GetAsync();
        var changed = await GetAsync();
        var cached = await GetAsync();

        changed.Body["state"].Should().Be("closed");
        cached.Body["state"].Should().Be("closed");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[2]["If-None-Match"].Should().Be(SecondEtag);
    }

    [TestCase("repos/dotnet/runtime")]
    [TestCase("repos/dotnet/runtime/pulls")]
    [TestCase(PullRequestPath + "/reviews")]
    [TestCase(PullRequestPath + "/files")]
    [TestCase("repos/dotnet/runtime/contents/eng/Version.Details.xml")]
    [TestCase("repos/dotnet/runtime/commits/abc123/statuses")]
    [TestCase("https://example.com/" + PullRequestPath)]
    [TestCase("http://api.github.com/" + PullRequestPath)]
    public async Task OtherEndpointsDoNotCacheAsync(string path)
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));

        await GetAsync(path);
        var second = await GetAsync(path);

        second.Body["state"].Should().Be("closed");
        _sentHeaders.Should().OnlyContain(headers => !headers.ContainsKey("If-None-Match"));
        _cache.Values.Should().BeEmpty();
    }

    [Test]
    public async Task WritesDoNotUseCachedEtagAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));

        await GetAsync();
        await _connection.Patch<Dictionary<string, string>>(new Uri(PullRequestPath, UriKind.Relative), new { state = "closed" });
        var afterWrite = await GetAsync();

        _sentHeaders[1].Should().NotContainKey("If-None-Match");
        _sentHeaders[2]["If-None-Match"].Should().Be(FirstEtag);
        afterWrite.Body["state"].Should().Be("closed");
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task ErrorsPropagateInsteadOfReturningCachedBodyAsync(HttpStatusCode status)
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(status, "{\"message\":\"request failed\"}"));
        await GetAsync();

        Func<Task> act = async () => await GetAsync();

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(status);
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
    }

    [Test]
    public async Task AuthorizationChangesStillRevalidateSharedResponseAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync();
        _connection.Credentials = new Credentials("another-test-token");

        var differentAuthorization = await GetAsync();
        _connection.Credentials = new Credentials("test-token");
        var originalAuthorization = await GetAsync();

        differentAuthorization.Body["state"].Should().Be("closed");
        originalAuthorization.Body["state"].Should().Be("closed");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[2]["If-None-Match"].Should().Be(SecondEtag);
    }

    [TestCase("repos/dotnet/aspnetcore/pulls/123")]
    [TestCase("repos/dotnet/runtime/pulls/456")]
    [TestCase(PullRequestPath + "/commits")]
    public async Task DifferentUrisHaveSeparateEntriesAsync(string otherPath)
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync();

        var other = await GetAsync(otherPath);
        var original = await GetAsync();

        other.Body["state"].Should().Be("closed");
        original.Body["state"].Should().Be("open");
        _sentHeaders[1].Should().NotContainKey("If-None-Match");
        _sentHeaders[2]["If-None-Match"].Should().Be(FirstEtag);
    }

    [Test]
    public async Task QueryParametersAndPaginationHaveSeparateEntriesAsync()
    {
        const string path = PullRequestPath + "/commits";
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync(path + "?page=1&per_page=100");

        var pageTwo = await GetAsync(path + "?page=2&per_page=100");
        var pageOne = await GetAsync(path + "?page=1&per_page=100");

        pageTwo.Body["state"].Should().Be("closed");
        pageOne.Body["state"].Should().Be("open");
        _sentHeaders[1].Should().NotContainKey("If-None-Match");
        _sentHeaders[2]["If-None-Match"].Should().Be(FirstEtag);
    }

    [Test]
    public async Task CachedPagesPreservePaginationLinksAsync()
    {
        const string path = PullRequestPath + "/commits";
        const string nextPage = "https://api.github.com/" + path + "?page=2";
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag,
            new Dictionary<string, string> { ["Link"] = $"<{nextPage}>; rel=\"next\"" },
            new Dictionary<string, Uri> { ["next"] = new Uri(nextPage) }));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync(path);

        var revalidated = await GetAsync(path);

        revalidated.HttpResponse.Headers["Link"].Should().Contain(nextPage);
        revalidated.HttpResponse.ApiInfo.Links["next"].Should().Be(new Uri(nextPage));
    }

    [Test]
    public async Task OctokitCommitPaginationContinuesThroughCachedPagesAsync()
    {
        const string nextPage = "https://api.github.com/" + PullRequestPath + "/commits?page=2";
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, "[{\"sha\":\"first-commit\"}]", FirstEtag,
            new Dictionary<string, string> { ["Link"] = $"<{nextPage}>; rel=\"next\"" },
            new Dictionary<string, Uri> { ["next"] = new Uri(nextPage) }));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, "[{\"sha\":\"second-commit\"}]", SecondEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        var client = new Octokit.GitHubClient(_connection);

        var first = await client.PullRequest.Commits("dotnet", "runtime", 123);
        var cached = await client.PullRequest.Commits("dotnet", "runtime", 123);

        first.Should().HaveCount(2);
        cached.Should().HaveCount(2);
        cached[0].Sha.Should().Be("first-commit");
        cached[1].Sha.Should().Be("second-commit");
        _sentHeaders[2]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[3]["If-None-Match"].Should().Be(SecondEtag);
    }

    [Test]
    public async Task NewReplicaUsesSerializedSharedCacheAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync();

        _connection = CreateConnection(new SerializedCache(_cache.Values));
        var cached = await GetAsync();

        cached.Body["state"].Should().Be("open");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
    }

    [Test]
    public async Task UriCacheSurvivesTokenRefreshAndRolloutAsync()
    {
        _connection = CreateConnection(productVersion: "1");
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        await GetAsync();

        _connection = CreateConnection(new SerializedCache(_cache.Values), productVersion: "2");
        _connection.Credentials = new Credentials("refreshed-test-token");
        var cached = await GetAsync();

        cached.Body["state"].Should().Be("open");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[1]["Authorization"].Should().Contain("refreshed-test-token");
    }

    [Test]
    public async Task NewIdentityWithoutAccessDoesNotReceiveCachedResponseAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"));
        await GetAsync();
        _connection.Credentials = new Credentials("different-test-token");

        Func<Task> act = async () => await GetAsync();

        await act.Should().ThrowAsync<NotFoundException>();
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[1]["Authorization"].Should().Contain("different-test-token");
    }

    [Test]
    public async Task RequestAwaitsDistributedCacheWriteAsync()
    {
        var write = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new Mock<ICache>();
        cache.Setup(value => value.TryGetAsync<CachedEntry>(It.IsAny<string>())).ReturnsAsync((CachedEntry)null);
        cache.Setup(value => value.TrySetAsync(It.IsAny<string>(), It.IsAny<CachedEntry>(), It.IsAny<TimeSpan?>()))
            .Returns(write.Task);
        _connection = CreateConnection(cache.Object);
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));

        var request = GetAsync();
        var waitedForWrite = !request.IsCompleted;
        write.SetResult(true);
        var response = await request;

        waitedForWrite.Should().BeTrue();
        response.Body["state"].Should().Be("open");
    }

    [Test]
    public async Task FailedCacheWriteIsLoggedAndDoesNotReplaceGitHubResponseAsync()
    {
        var logger = new Mock<ILogger>();
        var cache = new Mock<ICache>();
        cache.Setup(value => value.TryGetAsync<CachedEntry>(It.IsAny<string>())).ReturnsAsync((CachedEntry)null);
        cache.Setup(value => value.TrySetAsync(It.IsAny<string>(), It.IsAny<CachedEntry>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);
        _connection = CreateConnection(cache.Object, logger.Object);
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));

        var fresh = await GetAsync();

        fresh.Body["state"].Should().Be("open");
        logger.Verify(value => value.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    [Test]
    public async Task PreprocessingDoesNotChangeCachedResponseBodyAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.NotModified));
        var uri = new Uri(PullRequestPath, UriKind.Relative);

        var processed = await _connection.Get<Dictionary<string, string>>(
            uri, null, null, CancellationToken.None, body => ((string)body).Replace("open", "OPEN"));
        var raw = await GetAsync();

        processed.Body["state"].Should().Be("OPEN");
        raw.Body["state"].Should().Be("open");
        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
    }

    [Test]
    public async Task EvictedEntryUsesOrdinaryGetAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody, SecondEtag));
        await GetAsync();
        _cache.Values.Clear();

        var fresh = await GetAsync();

        fresh.Body["state"].Should().Be("closed");
        _sentHeaders[1].Should().NotContainKey("If-None-Match");
    }

    [Test]
    public async Task ResponseWithoutEtagDisablesConditionalGetAsync()
    {
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody, FirstEtag));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, ClosedBody));
        _responses.Enqueue(CreateResponse(HttpStatusCode.OK, OpenBody));

        await GetAsync();
        await GetAsync();
        await GetAsync();

        _sentHeaders[1]["If-None-Match"].Should().Be(FirstEtag);
        _sentHeaders[2].Should().NotContainKey("If-None-Match");
        _cache.Values.Should().ContainSingle();
    }

    [Test]
    public void ClientWithoutResponseCacheStillCreatesConnection()
    {
        var client = new GitHubClient(
            new ResolvedTokenProvider("test-token"),
            Mock.Of<IProcessManager>(),
            NullLogger.Instance,
            cache: null);

        var octokit = client.GetClient("dotnet", "runtime");

        octokit.Connection.Should().NotBeNull();
        octokit.Connection.Credentials.Password.Should().Be("test-token");
    }

    [Test]
    public async Task EntriesHaveBoundedRetentionAndDoNotExposeCredentialsInKeysAsync()
    {
        string key = null;
        CachedEntry entry = null;
        TimeSpan? expiration = null;
        var cache = new Mock<ICache>();
        cache.Setup(value => value.TrySetAsync(It.IsAny<string>(), It.IsAny<CachedEntry>(), It.IsAny<TimeSpan?>()))
            .Callback<string, CachedEntry, TimeSpan?>((cacheKey, value, retention) =>
            {
                key = cacheKey;
                entry = value;
                expiration = retention;
            })
            .ReturnsAsync(true);
        var request = new Request
        {
            Method = HttpMethod.Get,
            BaseAddress = new Uri("https://api.github.com"),
            Endpoint = new Uri(PullRequestPath, UriKind.Relative),
        };
        request.Headers["Authorization"] = "token test-token";
        var response = new CachedResponse.V1(
            OpenBody,
            new Dictionary<string, string> { ["ETag"] = FirstEtag },
            new ApiInfo(new Dictionary<string, Uri>(), [], [], FirstEtag, null),
            HttpStatusCode.OK,
            "application/json");

        await new GitHubResponseCache(cache.Object, NullLogger.Instance).SetAsync(request, response);

        expiration.Should().Be(TimeSpan.FromDays(1));
        entry.Body.Should().Be(OpenBody);
        entry.Etag.Should().Be(FirstEtag);
        key.Should().Be("GitHubConditionalResponse:v1:https://api.github.com/" + PullRequestPath);
        key.Should().NotContain("test-token");
    }

    [Test]
    public async Task CacheReadFailuresAreLoggedAndPropagateAsync()
    {
        var logger = new Mock<ILogger>();
        var cache = new Mock<ICache>();
        cache.Setup(value => value.TryGetAsync<CachedEntry>(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Cache unavailable"));
        _connection = CreateConnection(cache.Object, logger.Object);

        Func<Task> act = async () => await GetAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cache unavailable");
        _sentHeaders.Should().BeEmpty();
        logger.Verify(value => value.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    private Connection CreateConnection(ICache cache = null, ILogger logger = null, string productVersion = "1")
    {
        return new Connection(new ProductHeaderValue("cache-tests", productVersion),
            new GitHubConditionalRequestClient(
                _transport.Object, new GitHubResponseCache(cache ?? _cache, logger ?? NullLogger.Instance)))
        {
            Credentials = new Credentials("test-token"),
        };
    }

    private Task<IApiResponse<Dictionary<string, string>>> GetAsync(string path = PullRequestPath) =>
        _connection.Get<Dictionary<string, string>>(new Uri(path, UriKind.RelativeOrAbsolute), null);

    private static IResponse CreateResponse(
        HttpStatusCode status, string body = "", string etag = null,
        Dictionary<string, string> headers = null, Dictionary<string, Uri> links = null)
    {
        headers ??= [];
        if (etag != null)
        {
            headers["ETag"] = etag;
        }

        return new CachedResponse.V1(
            body,
            headers,
            new ApiInfo(links ?? new Dictionary<string, Uri>(), [], [], etag, null),
            status,
            "application/json");
    }

    private sealed class SerializedCache(Dictionary<string, string> values = null) : ICache
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public Dictionary<string, string> Values { get; } = values ?? [];

        public Task<T> TryGetAsync<T>(string key) where T : class
        {
            var value = Values.TryGetValue($"{typeof(T).Name}_{key}", out var json)
                ? JsonSerializer.Deserialize<T>(json, Options)
                : null;
            return Task.FromResult(value);
        }

        public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            Values[$"{typeof(T).Name}_{key}"] = JsonSerializer.Serialize(value, Options);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string key) => Task.FromResult(Values.Remove(key));
    }
}
