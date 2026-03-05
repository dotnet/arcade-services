// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("sql-pass", "DevPass1@", secret: true);

var sqlServer = builder.AddSqlServer("bi-mssql", password)
    .WithHostPort(11434)
    .WithContainerRuntimeArgs("--publish", "11433:1433")
    .WithDataVolume("build-insights-mssql-data")
    .WithLifetime(ContainerLifetime.Persistent);

var database = sqlServer.AddDatabase("BuildInsights");

var redisCache = builder
    .AddRedis("bi-redis", port: 55690);

var storage = builder.AddAzureStorage("bi-storage")
    .RunAsEmulator();

storage.AddBlobContainer(
    "previousbuildresultscache",
    "previousbuildresultscache");

var blobs = storage
    .AddBlobs("bi-blobs");

var queues = storage
    .AddQueues("bi-queues");

builder.AddProject<Projects.BuildInsights_Api>("buildInsightsApi")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues);

builder.AddProject<Projects.BuildInsights_KnownIssuesMonitor>("knownIssuesMonitor")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues)
    .WithExplicitStart();

builder.AddProject<Projects.BuildInsights_DummyApp>("dummyApp")
    .WithUrlForEndpoint("https", url => url.Url += "/status")
    .WithUrlForEndpoint("http", url => url.Url += "/status")
    .WithExternalHttpEndpoints()
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues);

builder.Build().Run();
