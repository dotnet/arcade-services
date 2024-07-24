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
    public void AreDependenciesRegistered()
    {
        // DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        // {
        //     IEnumerable<Type> optionTypes = Program.GetOptions().Concat(Program.GetVmrOptions());
        //     foreach (Type optionType in optionTypes)
        //     {
        //         var operationOption = (CommandLineOptions)Activator.CreateInstance(optionType);
        //         operationOption.RegisterServices(s);
        //         var provider = s.BuildServiceProvider();
        //         var operation = operationOption.GetOperation(provider);
        //         operation.Should().NotBeNull($"The operation {optionType.Name} should be registered.");
        //     }
        // },
        // out string message).Should().BeTrue(message);
    }
}
