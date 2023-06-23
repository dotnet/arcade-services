// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using VerifyNUnit;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GetSubscriptionsOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput = null!;
    private ServiceCollection _services = null!;
    private Mock<IRemote> _remoteMock = null!;


    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();

        _remoteMock = new Mock<IRemote>();
        _services = new ServiceCollection();
    }

    [TearDown]
    public void Teardown()
    {
        _consoleOutput.Dispose();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_empty_set()
    {
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new(), _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        logs.Should().Be($"No subscriptions found matching the specified criteria.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_AuthenticationException()
    {
        _remoteMock.Setup(t => t.GetDefaultChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new AuthenticationException("boo."));
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new(), _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        logs.Should().Be($"boo.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_Exception()
    {
        _remoteMock.Setup(t => t.GetDefaultChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new Exception("foo."));
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new(), _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        // Nothing is written to the console, but to ILogger.Error instead.
        logs.Should().BeEmpty();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_text()
    {
        Subscription subscription = new(Guid.Empty, true, "source", "target", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        List<Subscription> subscriptions = new()
        {
            subscription
        };

        _remoteMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new(), _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_json()
    {
        Subscription subscription = new(Guid.Empty, true, "source", "target", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        List<Subscription> subscriptions = new()
        {
            subscription
        };

        _remoteMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new() { OutputFormat = DarcOutputType.json }, _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_sorted_text()
    {
        Subscription subscription1 = new(Guid.Empty, true, "source2", "target2", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        Subscription subscription2 = new(Guid.Empty, true, "source1", "target1", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        List<Subscription> subscriptions = new()
        {
            subscription1,
            subscription2,
        };

        _remoteMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));
        _services.AddSingleton(_remoteMock.Object);

        GetSubscriptionsOperation operation = new(new() { OutputFormat = DarcOutputType.text }, _services);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }
}
