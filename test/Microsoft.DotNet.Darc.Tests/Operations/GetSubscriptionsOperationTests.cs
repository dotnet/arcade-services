// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using VerifyNUnit;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GetSubscriptionsOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput = null!;
    private Mock<IBarApiClient> _barMock = null!;
    private Mock<ILogger<GetSubscriptionsOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();

        _barMock = new();
        _loggerMock = new();
    }

    [TearDown]
    public void Teardown()
    {
        _consoleOutput.Dispose();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_empty_set()
    {
        var operation = new GetSubscriptionsOperation(new GetSubscriptionsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOutput();
        logs.Should().Be($"No subscriptions found matching the specified criteria.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_AuthenticationException()
    {
        _barMock.Setup(t => t.GetDefaultChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new AuthenticationException("boo."));

        var operation = new GetSubscriptionsOperation(new GetSubscriptionsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOutput();
        logs.Should().Be($"boo.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_Exception()
    {
        _barMock.Setup(t => t.GetDefaultChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new Exception("foo."));

        var operation = new GetSubscriptionsOperation(new GetSubscriptionsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOutput();
        // Nothing is written to the console, but to ILogger.Error instead.
        logs.Should().BeEmpty();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_text()
    {
        Subscription subscription = new(Guid.Empty, true, false, "source", "target", "test", string.Empty, null, null, [])
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };

        List<Subscription> subscriptions =
        [
            subscription
        ];

        _barMock
            .Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(subscriptions.AsEnumerable());

        var operation = new GetSubscriptionsOperation(new GetSubscriptionsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOutput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_json()
    {
        Subscription subscription = new(Guid.Empty, true, false, "source", "target", "test", null, null, string.Empty, ["Foo.Bar", "Bar.Xyz"])
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };

        List<Subscription> subscriptions =
        [
            subscription
        ];

        _barMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(subscriptions.AsEnumerable());

        var operation = new GetSubscriptionsOperation(new GetSubscriptionsCommandLineOptions { OutputFormat = DarcOutputType.json }, _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOutput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_sorted_text()
    {
        Subscription subscription1 = new(Guid.Empty, true, true, "source2", "target2", "test", "repo-name", null, null, [])
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };

        Subscription subscription2 = new(Guid.Empty, true, false, "source1", "target1", "test", string.Empty, null, null, [])
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };

        List<Subscription> subscriptions =
        [
            subscription1,
            subscription2,
        ];

        _barMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(subscriptions.AsEnumerable());

        var operation = new GetSubscriptionsOperation(
            new GetSubscriptionsCommandLineOptions()
            {
                OutputFormat = DarcOutputType.text
            },
            _barMock.Object,
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOutput();
        await Verifier.Verify(logs);
    }
}
