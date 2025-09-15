// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("refresh-config", HelpText = "Refresh PCS in-memory configuration from repository")]
internal class RefreshConfigurationOptions : PcsStatusOptions
{
    [Option("repo-uri", Required = true, HelpText = "Repository URI containing the configuration")]
    public required string RepoUri { get; init; }

    [Option("branch", Required = true, HelpText = "Branch containing the configuration")]
    public required string Branch { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => 
        new RefreshConfigurationOperation(
            sp.GetRequiredService<Microsoft.DotNet.ProductConstructionService.Client.IProductConstructionServiceApi>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RefreshConfigurationOperation>>(),
            RepoUri,
            Branch);
}