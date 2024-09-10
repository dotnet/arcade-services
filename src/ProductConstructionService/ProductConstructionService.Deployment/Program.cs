// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using ProductConstructionService.Deployment;


Parser.Default.ParseArguments<DeploymentOptions>(args)
    .WithParsed(options =>
    {
        var deployer = new Deployer(options);
        deployer.DeployAsync().GetAwaiter().GetResult();
    });


