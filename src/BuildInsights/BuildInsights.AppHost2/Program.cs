// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var redisCache = builder
    .AddRedis("redis", port: 55689);

var queues = builder.AddAzureStorage("storage")
    .RunAsEmulator()
    .AddQueues("queues");

builder.AddProject<Projects.ProductConstructionService_Api>("productConstructionServiceApi")
    .WithReference(queues)
    .WithReference(redisCache);

builder.Build().Run();
