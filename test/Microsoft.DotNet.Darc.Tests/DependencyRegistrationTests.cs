// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        {
            // We're using a VMR option here so we register all services
            // We need to specify something as the AzDo token, so we don't get a login prompt
            VmrPushCommandLineOptions options = new() { AzureDevOpsPat = "fake" };
            options.RegisterServices(s);
            var provider = s.BuildServiceProvider();
            Type[] optionTypes = Program.GetOptions().Concat(Program.GetVmrOptions()).ToArray();
            foreach (Type optionType in optionTypes)
            {
                CommandLineOptions operationOption =(CommandLineOptions) Activator.CreateInstance(optionType);
                var operation = operationOption.GetOperation(provider);
                operation.Should().NotBeNull($"The operation {optionType.Name} should be registered.");
            }
        },
        out string message).Should().BeTrue(message);
    }
}
