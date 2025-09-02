// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions;
using Microsoft.Extensions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;


/// <summary>
/// Unit tests for RemoteRepoBase constructor behavior regarding initialization of
/// TemporaryRepositoryPath and Cache properties.
/// </summary>
public class RemoteRepoBaseTests
{
    private sealed class TestRemoteRepo : RemoteRepoBase
    {
        public TestRemoteRepo(
            IRemoteTokenProvider remoteConfiguration,
            IProcessManager processManager,
            string temporaryRepositoryPath,
            IMemoryCache cache,
            ILogger logger)
            : base(remoteConfiguration, processManager, temporaryRepositoryPath, cache, logger)
        {
        }

        public string ExposeTemporaryRepositoryPath() => TemporaryRepositoryPath;

        public IMemoryCache ExposeCache() => Cache;
    }

    private static IEnumerable<string> NonNullPathValues()
    {
        yield return "C:\\temp\\repo";
        yield return string.Empty;
        yield return "   ";
        yield return "/very/long/path/with/special/chars!@#$%^&()[]{};,'`~";
        yield return new string('a', 1024);
        yield return "path-with-unicode-☃-snowman";
        yield return "relative/./path/../segment";
    }

    /// <summary>
    /// Ensures that when a non-null temporaryRepositoryPath is provided to the constructor,
    /// it is assigned verbatim to TemporaryRepositoryPath without validation or normalization.
    /// Inputs:
    ///  - Non-null strings including empty, whitespace-only, long, and special-character paths.
    /// Expected:
    ///  - Instance is constructed successfully.
    ///  - TemporaryRepositoryPath equals the provided value.
    ///  - Cache property references the same IMemoryCache instance passed in.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(NonNullPathValues))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoteRepoBase_NonNullTemporaryRepositoryPath_AssignedVerbatim(string inputPath)
    {
        // Arrange
        var remoteProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose).Object;
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var cacheMock = new Mock<IMemoryCache>(MockBehavior.Loose);
        var cache = cacheMock.Object;

        // Act
        var sut = new TestRemoteRepo(remoteProvider, processManager, inputPath, cache, logger);

        // Assert
        sut.Should().NotBeNull();
        sut.ExposeTemporaryRepositoryPath().Should().Be(inputPath);
        sut.ExposeCache().Should().BeSameAs(cache);
    }

    /// <summary>
    /// Verifies that when temporaryRepositoryPath is null, the constructor falls back to
    /// System.IO.Path.GetTempPath() for TemporaryRepositoryPath.
    /// Inputs:
    ///  - temporaryRepositoryPath = null
    /// Expected:
    ///  - TemporaryRepositoryPath equals Path.GetTempPath().
    ///  - Construction succeeds without throwing.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoteRepoBase_NullTemporaryRepositoryPath_UsesSystemTempPath()
    {
        // Arrange
        var remoteProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose).Object;
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var cacheMock = new Mock<IMemoryCache>(MockBehavior.Loose);
        var cache = cacheMock.Object;
        var expectedTemp = Path.GetTempPath();

        // Act
        var sut = new TestRemoteRepo(remoteProvider, processManager, null, cache, logger);

        // Assert
        sut.Should().NotBeNull();
        sut.ExposeTemporaryRepositoryPath().Should().Be(expectedTemp);
        sut.ExposeCache().Should().BeSameAs(cache);
    }

    /// <summary>
    /// Validates that the Cache property preserves the input value, including when a null IMemoryCache
    /// is provided. This ensures the constructor does not enforce non-null cache inputs.
    /// Inputs:
    ///  - temporaryRepositoryPath: a non-null value
    ///  - cache: null
    /// Expected:
    ///  - Construction succeeds without throwing.
    ///  - Cache property is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoteRepoBase_NullCache_PreservedAsNull()
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        IMemoryCache cache = null;
        string inputPath = "any-non-null-path";

        // Act
        var create = () => new TestRemoteRepo(
            remoteTokenProvider.Object,
            processManager.Object,
            inputPath,
            cache,
            logger.Object);

        var sut = create();

        // Assert
        create.Should().NotThrow();
        sut.ExposeTemporaryRepositoryPath().Should().Be(inputPath);
        sut.ExposeCache().Should().BeNull();
    }
}
