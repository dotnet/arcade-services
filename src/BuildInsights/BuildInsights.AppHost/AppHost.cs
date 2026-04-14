// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var buildInsightsApiHttpsPort = builder.Configuration.GetValue<int>("BuildInsightsApiHttpsPort");

var password = builder.AddParameter("sql-pass", "DevPass1@", secret: true);

var sqlServer = builder.AddSqlServer("mssql", password)
    .WithHostPort(11434)
    .WithContainerRuntimeArgs("--publish", "11433:1433")
    .WithDataVolume("build-insights-mssql-data")
    .WithLifetime(ContainerLifetime.Persistent);

var database = sqlServer.AddDatabase("bi-mssql", "BuildInsights");

var redisCache = builder.AddRedis("bi-redis", port: 55690);

var storage = builder.AddAzureStorage("bi-storage")
    .RunAsEmulator();

storage.AddBlobContainer(
    "previousbuildresultscache",
    "previousbuildresultscache");

var blobs = storage.AddBlobs("bi-blobs");

var queues = storage.AddQueues("bi-queues");

var buildInsightsApi = builder.AddProject<Projects.BuildInsights_Api>("buildInsightsApi")
    .WithHttpsEndpoint(port: buildInsightsApiHttpsPort, name: "buildInsightsApiHttps")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "buildInsightsApiHttps")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues);

builder.AddProject<Projects.BuildInsights_WebhookTunnel>("buildInsightsWebhookTunnel")
    .WithEnvironment("BUILD_INSIGHTS_API_PORT", buildInsightsApiHttpsPort.ToString())
    .WithEnvironment("BUILD_INSIGHTS_API_BASE_URL", "https://localhost:" + buildInsightsApiHttpsPort)
    .WaitFor(buildInsightsApi)
    .WithExplicitStart();

builder.AddProject<Projects.BuildInsights_KnownIssuesMonitor>("knownIssuesMonitor")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues)
    .WithExplicitStart();

builder.Build().Run();
