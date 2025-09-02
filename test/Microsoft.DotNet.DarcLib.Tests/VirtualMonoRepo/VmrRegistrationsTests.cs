// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;


public class VmrRegistrationsTests
{
    /// <summary>
    /// Tests registering of dependencies for GitNativeRepoCloner
    /// </summary>
    [Test]
    public void AddGitNativeRepoClonerSupportRegistrationIsCoherent()
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(sc => sc.AddGitNativeRepoClonerSupport(),
        out var message).Should().BeTrue(message);
    }

    /// <summary>
    /// Verifies that AddMultiVmrSupport:
    ///  - Normalizes tmpPath via Path.GetFullPath.
    ///  - Registers IVmrInfo and ISourceManifest as scoped services using factories.
    ///  - Returns the same IServiceCollection instance after registration.
    /// Inputs:
    ///  - Various relative and absolute tmpPath strings.
    /// Expected:
    ///  - Resolved IVmrInfo within a scope is non-null and its TmpPath equals Path.GetFullPath(input).
    ///  - Scoped lifetime semantics are respected (same instance within a scope, different across scopes).
    ///  - Resolved ISourceManifest is non-null with empty Repositories and Submodules.
    ///  - Returned IServiceCollection reference equals the originally provided collection.
    /// </summary>
    [TestCaseSource(nameof(TmpPathInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddMultiVmrSupport_RegistersScopedServicesAndNormalizesTmpPath(string tmpPathInput)
    {
        // Arrange
        var services = new ServiceCollection();
        var gitLocation = "git-custom";
        var expectedFullPath = Path.GetFullPath(tmpPathInput);

        // Act
        var returned = VmrRegistrations.AddMultiVmrSupport(services, tmpPathInput, gitLocation);
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var vmrInfo1a = scope1.ServiceProvider.GetService<IVmrInfo>();
        var vmrInfo1b = scope1.ServiceProvider.GetService<IVmrInfo>();
        var sourceManifest1 = scope1.ServiceProvider.GetService<ISourceManifest>();

        using var scope2 = provider.CreateScope();
        var vmrInfo2 = scope2.ServiceProvider.GetService<IVmrInfo>();

        // Assert
        // Returned service collection instance
        NUnit.Framework.Assert.That(object.ReferenceEquals(returned, services), NUnit.Framework.Is.True);

        // IVmrInfo is scoped, resolves, and TmpPath is normalized via GetFullPath
        NUnit.Framework.Assert.That(vmrInfo1a, NUnit.Framework.Is.Not.Null);
        NUnit.Framework.Assert.That(vmrInfo1b, NUnit.Framework.Is.Not.Null);
        NUnit.Framework.Assert.That(vmrInfo2, NUnit.Framework.Is.Not.Null);
        NUnit.Framework.Assert.That(vmrInfo1a, NUnit.Framework.Is.SameAs(vmrInfo1b), "Scoped lifetime should return the same instance within the same scope.");
        NUnit.Framework.Assert.That(vmrInfo1a, NUnit.Framework.Is.Not.SameAs(vmrInfo2), "Scoped lifetime should return different instances across scopes.");
        NUnit.Framework.Assert.That(vmrInfo1a.TmpPath.ToString(), NUnit.Framework.Is.EqualTo(expectedFullPath), "TmpPath should be normalized using Path.GetFullPath.");

        // ISourceManifest is scoped and starts with empty collections
        NUnit.Framework.Assert.That(sourceManifest1, NUnit.Framework.Is.Not.Null);
        NUnit.Framework.Assert.That(sourceManifest1.Repositories.Count, NUnit.Framework.Is.EqualTo(0));
        NUnit.Framework.Assert.That(sourceManifest1.Submodules.Count, NUnit.Framework.Is.EqualTo(0));
    }

    private static string[] TmpPathInputs()
    {
        return new[]
        {
                ".",                         // relative current directory
                "relativeDir",               // simple relative path
                Path.Combine("rel", "sub"),  // relative nested path
                Path.GetTempPath(),          // absolute path
                ""                           // empty string, normalized to current directory
            };
    }

    /// <summary>
    /// Verifies that AddSingleVmrSupport:
    /// - Registers IVmrInfo as a singleton with absolute FullPath values derived from vmrPath and tmpPath.
    /// - Registers ISourceManifest as a scoped service that is instantiated per scope and loads from SourceManifestPath.
    /// - Returns the same IServiceCollection instance provided.
    /// Inputs:
    ///  - gitHubToken and azureDevOpsToken combinations (null or non-null).
    ///  - useRelativePaths toggles between relative and absolute vmr/tmp path inputs.
    /// Expected:
    ///  - Returned IServiceCollection is the original instance.
    ///  - Resolving IVmrInfo twice yields the same instance; VmrPath/TmpPath are absolute paths equal to Path.GetFullPath inputs.
    ///  - Resolving ISourceManifest in separate scopes yields different instances and, when the file does not exist, empty Repositories/Submodules.
    /// </summary>
    [TestCase(true, null, null, TestName = "AddSingleVmrSupport_RelativePaths_TokensNull_RegistersSingletonAndScopedCorrectly")]
    [TestCase(false, "gh-token", "ado-token", TestName = "AddSingleVmrSupport_AbsolutePaths_TokensProvided_RegistersSingletonAndScopedCorrectly")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddSingleVmrSupport_RegistersSingletonAndScopedServices_WithExpectedPathsAndLifetimes(
        bool useRelativePaths,
        string gitHubToken,
        string azureDevOpsToken)
    {
        // Arrange
        var services = new ServiceCollection();
        var gitLocation = "git";

        var unique = Guid.NewGuid().ToString("N");
        var relVmr = $"vmr-{unique}";
        var relTmp = $"tmp-{unique}";
        var absVmr = Path.Combine(Path.GetTempPath(), $"vmr-{unique}");
        var absTmp = Path.Combine(Path.GetTempPath(), $"tmp-{unique}");

        var vmrPath = useRelativePaths ? relVmr : absVmr;
        var tmpPath = useRelativePaths ? relTmp : absTmp;

        var expectedVmrFullPath = Path.GetFullPath(vmrPath);
        var expectedTmpFullPath = Path.GetFullPath(tmpPath);

        // Act
        var returned = services.AddSingleVmrSupport(gitLocation, vmrPath, tmpPath, gitHubToken, azureDevOpsToken);
        var provider = services.BuildServiceProvider();

        var vmrInfo1 = provider.GetRequiredService<IVmrInfo>();
        var vmrInfo2 = provider.GetRequiredService<IVmrInfo>();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var manifestA = scopeA.ServiceProvider.GetRequiredService<ISourceManifest>();
        var manifestB = scopeB.ServiceProvider.GetRequiredService<ISourceManifest>();

        // Assert
        returned.Should().BeSameAs(services);

        vmrInfo1.Should().NotBeNull();
        vmrInfo1.Should().BeSameAs(vmrInfo2);
        vmrInfo1.VmrPath.ToString().Should().Be(expectedVmrFullPath);
        vmrInfo1.TmpPath.ToString().Should().Be(expectedTmpFullPath);

        manifestA.Should().NotBeNull();
        manifestB.Should().NotBeNull();
        manifestA.Should().NotBeSameAs(manifestB);
        manifestA.Repositories.Count.Should().Be(0);
        manifestA.Submodules.Count.Should().Be(0);
    }

    /// <summary>
    /// Ensures AddGitNativeRepoClonerSupport registers all required services
    /// and that resolutions succeed when IAzureDevOpsTokenProvider is available.
    /// Inputs:
    ///  - ServiceCollection with logging and a singleton IAzureDevOpsTokenProvider.
    /// Expected:
    ///  - IGitRepoCloner resolves as GitNativeRepoCloner (transient).
    ///  - IRemoteTokenProvider resolves as RemoteTokenProvider (singleton across resolves).
    ///  - ILocalGitClient resolves as LocalGitClient (transient).
    ///  - IProcessManager resolves as ProcessManager with GitExecutable == "git" (transient).
    ///  - ITelemetryRecorder resolves as NoTelemetryRecorder.
    ///  - IFileSystem resolves as FileSystem.
    ///  - Non-generic ILogger resolves (via ILogger<VmrManagerBase> mapping).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddGitNativeRepoClonerSupport_WithAzdoTokenProvider_ResolvesExpectedServicesAndLifetimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var azdoTokenProvider = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict).Object;
        services.AddSingleton(azdoTokenProvider);

        // Act
        services.AddGitNativeRepoClonerSupport();
        var provider = services.BuildServiceProvider();

        var cloner1 = provider.GetRequiredService<IGitRepoCloner>();
        var cloner2 = provider.GetRequiredService<IGitRepoCloner>();

        var remoteTokenProvider1 = provider.GetRequiredService<IRemoteTokenProvider>();
        var remoteTokenProvider2 = provider.GetRequiredService<IRemoteTokenProvider>();

        var localGitClient1 = provider.GetRequiredService<ILocalGitClient>();
        var localGitClient2 = provider.GetRequiredService<ILocalGitClient>();

        var processManager = provider.GetRequiredService<IProcessManager>();
        var telemetryRecorder = provider.GetRequiredService<ITelemetryRecorder>();
        var fileSystem = provider.GetRequiredService<IFileSystem>();
        var logger = provider.GetRequiredService<ILogger>();

        // Assert
        Assert.That(cloner1, Is.Not.Null);
        Assert.That(cloner1, Is.InstanceOf<GitNativeRepoCloner>());
        Assert.That(cloner2, Is.Not.SameAs(cloner1), "IGitRepoCloner should be transient");

        Assert.That(remoteTokenProvider1, Is.Not.Null);
        Assert.That(remoteTokenProvider1, Is.InstanceOf<RemoteTokenProvider>());
        Assert.That(remoteTokenProvider2, Is.SameAs(remoteTokenProvider1), "IRemoteTokenProvider should be singleton");

        Assert.That(localGitClient1, Is.Not.Null);
        Assert.That(localGitClient1, Is.InstanceOf<LocalGitClient>());
        Assert.That(localGitClient2, Is.Not.SameAs(localGitClient1), "ILocalGitClient should be transient");

        Assert.That(processManager, Is.Not.Null);
        Assert.That(processManager, Is.InstanceOf<ProcessManager>());
        Assert.That(processManager.GitExecutable, Is.EqualTo("git"));

        Assert.That(telemetryRecorder, Is.InstanceOf<NoTelemetryRecorder>());
        Assert.That(fileSystem, Is.InstanceOf<FileSystem>());
        Assert.That(logger, Is.Not.Null);
    }

    /// <summary>
    /// Validates that the registration assumes a pre-registered IAzureDevOpsTokenProvider.
    /// Inputs:
    ///  - ServiceCollection with logging, but without IAzureDevOpsTokenProvider.
    /// Expected:
    ///  - Resolving IRemoteTokenProvider throws InvalidOperationException because its factory
    ///    requires IAzureDevOpsTokenProvider via GetRequiredService.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddGitNativeRepoClonerSupport_WithoutAzdoTokenProvider_ResolvingIRemoteTokenProviderThrows()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddGitNativeRepoClonerSupport();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRemoteTokenProvider>());
    }

    /// <summary>
    /// Ensures TryAdd semantics: existing registrations are not overridden.
    /// Inputs:
    ///  - ServiceCollection with logging, singleton IAzureDevOpsTokenProvider, and a pre-registered singleton IProcessManager.
    /// Expected:
    ///  - Resolved IProcessManager is the pre-registered instance and not replaced by ProcessManager.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddGitNativeRepoClonerSupport_PreRegisteredIProcessManager_NotOverridden()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var azdoTokenProvider = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict).Object;
        services.AddSingleton(azdoTokenProvider);

        var customPm = new Mock<IProcessManager>(MockBehavior.Strict);
        customPm.SetupGet(m => m.GitExecutable).Returns("custom");
        services.AddSingleton<IProcessManager>(customPm.Object);

        // Act
        services.AddGitNativeRepoClonerSupport();
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IProcessManager>();

        // Assert
        Assert.That(resolved, Is.SameAs(customPm.Object));
        Assert.That(resolved, Is.Not.InstanceOf<ProcessManager>());
        Assert.That(resolved.GitExecutable, Is.EqualTo("custom"));
    }
}
