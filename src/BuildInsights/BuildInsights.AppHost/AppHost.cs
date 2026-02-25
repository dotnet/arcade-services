// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var redisCache = builder
    .AddRedis("redis", port: 55690);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

storage.AddBlobContainer(
    "previousbuildresultscache",
    "previousbuildresultscache");

var blobs = storage
    .AddBlobs("blobs");

var queues = storage
    .AddQueues("queues");

builder.AddProject<Projects.BuildInsights_Api>("buildInsightsApi")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(redisCache)
    .WaitFor(blobs)
    .WaitFor(queues);

builder.Build().Run();
