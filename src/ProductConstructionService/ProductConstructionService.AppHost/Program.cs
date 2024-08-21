// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var redisCache = builder.AddRedis("redis");
var queues = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithImageTag("3.31.0")) // Workaround for https://github.com/dotnet/aspire/issues/5078
    .AddQueues("queues");

builder.AddProject<Projects.ProductConstructionService_Api>("productConstructionServiceApi")
    .WithReference(queues)
    .WithReference(redisCache);

builder.Build().Run();
