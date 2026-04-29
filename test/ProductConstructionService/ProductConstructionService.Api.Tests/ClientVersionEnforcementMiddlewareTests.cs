// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Maestro.Common;
using Maestro.Services.Common.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class ClientVersionEnforcementMiddlewareTests
{
    private Mock<IRedisCacheFactory> _mockFactory = null!;
    private Mock<IRedisCache> _mockCache = null!;
    private ClientVersionEnforcementMiddleware _middleware = null!;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IRedisCacheFactory>();
        _mockCache = new Mock<IRedisCache>();
        _mockFactory
            .Setup(f => f.Create(MinClientVersionConstants.DarcMinVersionRedisKey))
            .Returns(_mockCache.Object);
        _middleware = new ClientVersionEnforcementMiddleware(
            _mockFactory.Object,
            NullLogger<ClientVersionEnforcementMiddleware>.Instance);
    }

    private static HttpContext CreateContext(string path, string? clientName = null, string? clientVersion = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (clientName is not null)
        {
            ctx.Request.Headers[MinClientVersionConstants.ClientNameHeader] = clientName;
        }
        if (clientVersion is not null)
        {
            ctx.Request.Headers[MinClientVersionConstants.ClientVersionHeader] = clientVersion;
        }
        return ctx;
    }

    private static (RequestDelegate Delegate, Func<bool> WasCalled) CreateNext()
    {
        var called = false;
        RequestDelegate del = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };
        return (del, () => called);
    }

    [Test]
    public async Task NonApiPath_PassesThrough()
    {
        var ctx = CreateContext("/swagger/index.html", "darc", "0.0.1");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("99.0.0");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        _mockCache.Verify(c => c.TryGetAsync(), Times.Never);
    }

    [Test]
    public async Task MissingHeaders_PassesThrough()
    {
        var ctx = CreateContext("/api/builds");
        var (next, wasCalled) = CreateNext();

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
        _mockCache.Verify(c => c.TryGetAsync(), Times.Never);
    }

    [Test]
    public async Task NonDarcClient_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "some-other-client", "0.0.1");
        var (next, wasCalled) = CreateNext();

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
        _mockCache.Verify(c => c.TryGetAsync(), Times.Never);
    }

    [Test]
    public async Task DevVersion_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "0.0.99-dev");
        var (next, wasCalled) = CreateNext();

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
        _mockCache.Verify(c => c.TryGetAsync(), Times.Never);
    }

    [Test]
    public async Task DevVersionDifferentCase_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "0.0.99-DEV");
        var (next, wasCalled) = CreateNext();

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
    }

    [Test]
    public async Task RedisKeyMissing_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "1.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync((string?)null);

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
    }

    [Test]
    public async Task RedisLookupThrows_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "1.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ThrowsAsync(new InvalidOperationException("redis down"));

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
    }

    [Test]
    public async Task UnparseableClientVersion_Returns426()
    {
        var ctx = CreateContext("/api/builds", "darc", "not-a-version!!");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.0.0");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status426UpgradeRequired);
        var error = await ReadErrorAsync(ctx);
        error!.Message.Should().Contain("could not be parsed");
        // Min header is not set when client version is unparseable.
        ctx.Response.Headers.ContainsKey(MinClientVersionConstants.MinimumClientVersionHeader).Should().BeFalse();
    }

    [Test]
    public async Task UnparseableMinVersion_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "1.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("garbage-version");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Test]
    public async Task ClientBelowMinimum_Returns426WithHeader()
    {
        var ctx = CreateContext("/api/builds", "darc", "1.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.2.3");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status426UpgradeRequired);
        ctx.Response.Headers[MinClientVersionConstants.MinimumClientVersionHeader].ToString().Should().Be("1.2.3");
        ctx.Response.ContentType.Should().Be("application/json");
        var error = await ReadErrorAsync(ctx);
        error!.Message.Should().Contain("1.0.0");
        error.Message.Should().Contain("1.2.3");
    }

    [Test]
    public async Task ClientAtMinimum_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "1.2.3");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.2.3");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
    }

    [Test]
    public async Task ClientAboveMinimum_PassesThrough()
    {
        var ctx = CreateContext("/api/builds", "darc", "2.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.2.3");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeTrue();
    }

    [Test]
    public async Task DarcClientNameCaseInsensitive_StillEnforced()
    {
        var ctx = CreateContext("/api/builds", "DARC", "1.0.0");
        var (next, wasCalled) = CreateNext();
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.2.3");

        await _middleware.InvokeAsync(ctx, next);

        wasCalled().Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status426UpgradeRequired);
    }

    private static async Task<ApiError?> ReadErrorAsync(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiError>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
