// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using FlatFlowMigrationCli.Operations;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ProductConstructionService.Common;
using Tools.Common;

namespace FlatFlowMigrationCli.Options;

internal abstract class Options
{
    [Option("pcsUri", Required = false, Default = "https://maestro.dot.net/", HelpText = "PCS base URI, defaults to the Prod PCS")]
    public required string PcsUri { get; init; }

    public virtual Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(PcsApiFactory.GetAuthenticated(
            PcsUri,
            accessToken: null,
            managedIdentityId: null,
            disableInteractiveAuth: false));

        services.AddLogging(logging => logging.AddConsole());

        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>());

        services.AddTransient<VmrDependencyResolver>();

        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<MigrateOperation>()
            .Build();
        var gitHubToken = userSecrets["GITHUB_TOKEN"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        services.AddTransient(sp =>
            new GitHubClient(new ResolvedTokenProvider(gitHubToken),
            sp.GetRequiredService<IProcessManager>(),
            sp.GetRequiredService<ILogger<GitHubClient>>(),
            null));

        services.AddMultiVmrSupport(Path.GetTempPath());

        return Task.FromResult(services);
    }

    public abstract IOperation GetOperation(IServiceProvider sp);
}
