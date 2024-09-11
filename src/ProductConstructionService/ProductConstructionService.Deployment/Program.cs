// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using CommandLine;
using Maestro.Common.AppCredentials;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Deployment;


Parser.Default.ParseArguments<DeploymentOptions>(args)
    .WithParsed(options =>
    {
        string APP_CLIENT_ID = "baf98f1b-374e-487d-af42-aa33807f11e4";

        string APP_AUDIENCE = $"{APP_CLIENT_ID}/.default";
        string USER_AUDIENCE = $"api://{APP_CLIENT_ID}/Maestro.User";

        TokenRequestContext appRequestContext = new TokenRequestContext(new string[] { APP_AUDIENCE });
        TokenRequestContext userRequestContext = new TokenRequestContext(new string[] { USER_AUDIENCE });

        var credentialOptions = new InteractiveBrowserCredentialOptions
        {
            TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47",
            ClientId = APP_CLIENT_ID,
            RedirectUri = new Uri("http://localhost"),
        };

        var credentials = new InteractiveBrowserCredential(credentialOptions);

        options.PcsToken = credentials.GetToken(userRequestContext).Token;
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ProcessManager processManager = new ProcessManager(loggerFactory.CreateLogger(""), "");
        var deployer = new Deployer(options, processManager);
        deployer.DeployAsync().GetAwaiter().GetResult();
    });


