// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
using Moq;
using NUnit.Framework;
using VerifyNUnit;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GetSubscriptionsOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput;

    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();
    }

    [TearDown]
    public void Teardown()
    {
        _consoleOutput.Dispose();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_empty_set()
    {
        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Returns(Task.FromResult(new List<Subscription>().AsEnumerable()));

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        logs.Should().Be($"No subscriptions found matching the specified criteria.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_AuthenticationException()
    {
        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Throws(new AuthenticationException("boo."));

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        logs.Should().Be($"boo.{Environment.NewLine}");
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_ErrorCode_for_Exception()
    {
        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Throws(new Exception("foo."));

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        // Nothing is written to the console, but to ILogger.Error instead.
        logs.Should().BeEmpty();
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_text()
    {
        List<Subscription> subscriptions = new();

        Subscription subscription = new(Guid.Empty, true, "source", "target", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };

        subscriptions.Add(subscription);

        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_json()
    {
        List<Subscription> subscriptions = new();

        Subscription subscription = new(Guid.Empty, true, "source", "target", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };

        subscriptions.Add(subscription);

        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));
        optionsMock.Object.OutputFormat = DarcOutputType.json;

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetSubscriptionsOperationTests_ExecuteAsync_returns_sorted_text()
    {
        List<Subscription> subscriptions = new();

        Subscription subscription = new(Guid.Empty, true, "source2", "target2", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        subscriptions.Add(subscription);

        subscription = new(Guid.Empty, true, "source1", "target1", "test", string.Empty)
        {
            Channel = new(id: 1, name: "name", classification: "classification"),
            Policy = new(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = ImmutableList<MergePolicy>.Empty
            }
        };
        subscriptions.Add(subscription);

        var optionsMock = new Mock<GetSubscriptionsCommandLineOptions>();
        optionsMock.Setup(t => t.FilterSubscriptions(It.IsAny<IRemote>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));

        GetSubscriptionsOperation operation = new(optionsMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }
}
