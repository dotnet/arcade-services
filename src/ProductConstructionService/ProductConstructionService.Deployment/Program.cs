// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using CommandLine;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Deployment;
using StackExchange.Redis;

return Parser.Default.ParseArguments<DeploymentOptions>(args)
    .MapResult((options) =>
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ProcessManager processManager = new ProcessManager(loggerFactory.CreateLogger(string.Empty), string.Empty);

        var redisConfig = ConfigurationOptions.Parse(options.RedisConnectionString);
        AzureCacheOptions azureOptions = new()
        {
            TokenCredential = new AzureCliCredential()
        };
        redisConfig.ConfigureForAzureAsync(azureOptions).GetAwaiter().GetResult();
        RedisCacheFactory redisCacheFactory = new(redisConfig, LoggerFactory.Create(config => config.AddConsole()).CreateLogger<RedisCache>());
        var cache = redisCacheFactory.Create("dkurepa");
        cache.SetAsync("test").GetAwaiter().GetResult();

        var pcsClient = ProductConstructionService.Client.PcsApiFactory.GetAuthenticated(
            accessToken: null,
            managedIdentityId: null,
            disableInteractiveAuth: options.IsCi);

        var deployer = new Deployer(options, processManager, pcsClient);
        return deployer.DeployAsync().GetAwaiter().GetResult();
    },
    (_) => -1);


