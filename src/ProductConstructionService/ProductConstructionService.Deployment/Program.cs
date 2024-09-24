// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Deployment;

return Parser.Default.ParseArguments<DeploymentOptions>(args)
    .MapResult((options) =>
    {
    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    ProcessManager processManager = new ProcessManager(loggerFactory.CreateLogger(string.Empty), string.Empty);

    var pcsClient = ProductConstructionService.Client.PcsApiFactory.GetAuthenticated(
        accessToken: null,
        managedIdentityId: null,
        disableInteractiveAuth: options.IsCi);

    var deployer = new Deployer(options, processManager, pcsClient);
    return deployer.DeployAsync().GetAwaiter().GetResult();
    },
    (_) => -1);


