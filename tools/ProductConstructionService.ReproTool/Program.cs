// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool;

Parser.Default.ParseArguments<ReproToolOptions>(args)
    .WithParsed<ReproToolOptions>(o =>
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<ReproTool>()
            .Build();
        o.GitHubToken ??= userSecrets["GITHUB_TOKEN"];
        o.GitHubToken ??= Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        ArgumentNullException.ThrowIfNull(o.GitHubToken, "GitHub must be provided via env variable, user secret or an option");

        var services = new ServiceCollection();

        services.RegisterServices(o);

        var provider = services.BuildServiceProvider();

        ActivatorUtilities.CreateInstance<ReproTool>(provider).ReproduceCodeFlow().GetAwaiter().GetResult();
    });
