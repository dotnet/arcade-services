// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;

[Verb("copy-build", HelpText = "Copies a production BAR build into local PCS using its maestro-auth-test repository")]
internal class CopyBuildOptions : Options
{
    [Option("build", HelpText = "Production BAR build ID to copy", Required = true)]
    public int BuildId { get; init; }

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<CopyBuildOperation>(sp, this);
}
