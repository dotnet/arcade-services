// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

if (WebhookTunnelCommand.ShouldRun(args))
{
    await WebhookTunnelCommand.RunAsync(args[1..]);
    return;
}

var builder = DistributedApplication.CreateBuilder(args);

const int BuildInsightsApiHttpsPort = 53180;
var appHostAssemblyPath = Path.Combine(AppContext.BaseDirectory, "BuildInsights.AppHost.dll");

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
    .WithHttpsEndpoint(port: BuildInsightsApiHttpsPort, name: "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(queues)
    .WithReference(redisCache)
    .WaitFor(database)
    .WaitFor(redisCache)
    .WaitFor(queues);

builder.AddExecutable(
        "buildInsightsWebhookTunnel",
        "dotnet",
        AppContext.BaseDirectory,
        appHostAssemblyPath,
        "webhook-tunnel",
        "--port",
        BuildInsightsApiHttpsPort.ToString())
    .WithEnvironment("BUILD_INSIGHTS_API_PORT", BuildInsightsApiHttpsPort.ToString())
    .WithEnvironment("BUILD_INSIGHTS_API_BASE_URL", "https://localhost:" + BuildInsightsApiHttpsPort)
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
