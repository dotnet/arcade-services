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
    [Test]
    [Ignore("Test skipped because it hangs in CI. https://github.com/dotnet/arcade-services/issues/3745")]
    public void AreDependenciesRegistered()
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(services =>
        {
            // Tests instantiating the operations
            IEnumerable<Type> optionTypes = Program.GetOptions().Concat(Program.GetVmrOptions());
            foreach (Type optionType in optionTypes)
            {
                // Register the option type
                services.AddTransient(optionType);

                var operationOption = (CommandLineOptions) Activator.CreateInstance(optionType);
                operationOption.RegisterServices(services);
                var provider = services.BuildServiceProvider();

                // Verify we can create the operation
                var operation = operationOption.GetOperation(provider);
                operation.Should().NotBeNull($"Operation of {optionType.Name} could not be created");
                services.AddTransient(operation.GetType());
            }
        },
        out string message).Should().BeTrue(message);
    }
}
