// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Deployment;


Parser.Default.ParseArguments<DeploymentOptions>(args)
    .WithParsed(options =>
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ProcessManager processManager = new ProcessManager(loggerFactory.CreateLogger(string.Empty), string.Empty);

        var deployer = new Deployer(options, processManager);
        deployer.DeployAsync().GetAwaiter().GetResult();
    });


