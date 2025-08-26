// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
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
public class GetChannelsOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput = null!;
    private Mock<IBarApiClient> _barMock = null!;
    private Mock<ILogger<GetChannelOperation>> _loggerMock = null!;

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
    public async Task GetChannelsOperation_ExecuteAsync_returns_ErrorCode_for_AuthenticationException()
    {
        _barMock.Setup(m => m.GetChannelsAsync(It.IsAny<string>()))
            .Throws(new AuthenticationException("Authentication error."));

        var operation = new GetChannelsOperation(new GetChannelsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        logs.Should().Be($"Authentication error.{Environment.NewLine}");
    }

    [Test]
    public async Task GetChannelsOperation_ExecuteAsync_returns_ErrorCode_for_Exception()
    {
        _barMock.Setup(m => m.GetChannelsAsync(It.IsAny<string>()))
            .Throws(new Exception("General error."));

        var operation = new GetChannelsOperation(new GetChannelsCommandLineOptions(), _barMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);

        var logs = _consoleOutput.GetOuput();
        // Error is logged to ILogger, not console
        logs.Should().BeEmpty();
    }

    [Test]
    public async Task GetChannelsOperation_ExecuteAsync_returns_json()
    {
        List<Channel> channels =
        [
            new Channel(id: 1, name: ".NET 8", classification: "product"),
            new Channel(id: 2, name: "VS Main", classification: "product"),
            new Channel(id: 3, name: "Test Channel", classification: "test"),
        ];

        _barMock.Setup(m => m.GetChannelsAsync(It.IsAny<string>()))
            .ReturnsAsync(channels.AsEnumerable());

        var operation = new GetChannelsOperation(
            new GetChannelsCommandLineOptions { OutputFormat = DarcOutputType.json }, 
            _barMock.Object, 
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetChannelsOperation_ExecuteAsync_returns_text()
    {
        List<Channel> channels =
        [
            new Channel(id: 1, name: ".NET 8", classification: "product"),
            new Channel(id: 2, name: "VS Main", classification: "product"),
            new Channel(id: 3, name: "Test Channel", classification: "test"),
        ];

        _barMock.Setup(m => m.GetChannelsAsync(It.IsAny<string>()))
            .ReturnsAsync(channels.AsEnumerable());

        var operation = new GetChannelsOperation(
            new GetChannelsCommandLineOptions { OutputFormat = DarcOutputType.text }, 
            _barMock.Object, 
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }

    [Test]
    public async Task GetChannelsOperation_ExecuteAsync_returns_grouped_text()
    {
        List<Channel> channels =
        [
            new Channel(id: 1, name: ".NET 9", classification: "product"),
            new Channel(id: 2, name: ".NET 8", classification: "product"),
            new Channel(id: 3, name: "VS Main", classification: "product"),
            new Channel(id: 4, name: "VS 17.12", classification: "product"),
            new Channel(id: 5, name: "Windows 11", classification: "product"),
            new Channel(id: 6, name: "Other Channel", classification: "product"),
            new Channel(id: 7, name: "Test Channel", classification: "test"),
        ];

        _barMock.Setup(m => m.GetChannelsAsync(It.IsAny<string>()))
            .ReturnsAsync(channels.AsEnumerable());

        var operation = new GetChannelsOperation(
            new GetChannelsCommandLineOptions { OutputFormat = DarcOutputType.text }, 
            _barMock.Object, 
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var logs = _consoleOutput.GetOuput();
        await Verifier.Verify(logs);
    }
}