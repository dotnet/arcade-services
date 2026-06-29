// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    /// <summary>
    /// Tests instantiating the operations
    /// </summary>
    [Test]
    public void AreDarcOperationsRegistered()
    {
        foreach (Type optionType in Program.GetOptions().Concat(Program.GetVmrOptions()))
        {
            DependencyInjectionValidation.IsDependencyResolutionCoherent(services =>
            {
                // Register the option type
                services.AddTransient(optionType);

                var operationOption = (CommandLineOptions)Activator.CreateInstance(optionType);
                // Set IsCi to true to avoid login pop up
                operationOption.IsCi = true;
                operationOption.RegisterServices(services);
                var provider = services.BuildServiceProvider();

                // Verify we can create the operation
                var operation = operationOption.GetOperation(provider);
                operation.Should().NotBeNull($"Operation for {optionType.Name} could not be created");
                services.AddTransient(operation.GetType());
            },
            out string message).Should().BeTrue(message);
        }
    }
}
