// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using NUnit.Framework;

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
}
