// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using FlatFlowMigrationCli.Operations;

namespace FlatFlowMigrationCli.Options;

[Verb("test-forward-flow", HelpText = "Triggers forward flow PRs against a test repository")]
internal class TestForwardFowOptions : FlatFlowMigrationCliOptions
{
    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<TestForwardFlowOperation>(sp, this);
}
