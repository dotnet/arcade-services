// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;
using ProductConstructionService.ReproTool.Options;
using Tools.Common;

Type[] options =
[
    typeof(ReproOptions),
    typeof(FlowCommitOptions),
];

Parser.Default.ParseArguments(args, options)
    .MapResult((Options o) =>
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<ReproOperation>()
            .Build();
        o.GitHubToken ??= userSecrets["GITHUB_TOKEN"];
        o.GitHubToken ??= Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        ArgumentNullException.ThrowIfNull(o.GitHubToken, "GitHub must be provided via env variable, user secret or an option");

        var services = new ServiceCollection();

        o.RegisterServices(services);
        services.AddSingleton<VmrDependencyResolver>();

        services.AddCodeflow(Path.GetTempPath());

        var provider = services.BuildServiceProvider();

        o.GetOperation(provider).RunAsync().GetAwaiter().GetResult();

        return 0;
    },
    (_) => -1);
