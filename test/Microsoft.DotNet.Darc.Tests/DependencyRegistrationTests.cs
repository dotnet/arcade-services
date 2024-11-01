// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test, TestCaseSource("GetDarcOperations")]
    public void IsDarcOperationRegistered(Type darcOperation)
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(services =>
        {
            // Register the option type
            services.AddTransient(darcOperation);

            var operationOption = (CommandLineOptions)Activator.CreateInstance(darcOperation);
            // Set IsCi to true to avoid login pop up
            operationOption.IsCi = true;
            operationOption.RegisterServices(services);
            var provider = services.BuildServiceProvider();

            // Verify we can create the operation
            var operation = operationOption.GetOperation(provider);
            operation.Should().NotBeNull($"Operation {darcOperation.Name} could not be created");
            services.AddTransient(operation.GetType());
        },
        out string message).Should().BeTrue(message);
    }

    public static IEnumerable<Type> GetDarcOperations()
    {
        return Program.GetOptions().Concat(Program.GetVmrOptions());
    }
}
