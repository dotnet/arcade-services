// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var vmrPath = builder.Configuration[VmrConfiguration.VmrPathKey]
    ?? throw new ArgumentException($"{VmrConfiguration.VmrPathKey} environmental variable must be set");
var tmpPath = builder.Configuration[VmrConfiguration.TmpPathKey]
    ?? throw new ArgumentException($"{VmrConfiguration.TmpPathKey} environmental variable must be set");
var vmrUri = builder.Configuration[VmrConfiguration.VmrUriKey]
    ?? throw new ArgumentException($"{VmrConfiguration.VmrUriKey} environmental variable must be set");

var managedIdentityClientId = builder.Configuration["ManagedIdentityClientId"] ?? string.Empty;
DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });

builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    credential);

builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
{
    options.UseSqlServer(builder.Configuration["build-asset-registry-sql-connection-string"] ?? string.Empty);
});

builder.AddTelemetry();

builder.AddVmrRegistrations(vmrPath, tmpPath, vmrUri);

builder.AddWorkitemQueues(credential);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// When running locally, create the workitem queue, if it doesn't already exist
if (app.Environment.IsDevelopment())
{
    var queueServiceClient = app.Services.GetRequiredService<QueueServiceClient>();
    var queueClient = queueServiceClient.GetQueueClient(app.Configuration[QueueConfiguration.JobQueueConfigurationKey]);
    await queueClient.CreateIfNotExistsAsync();
}
// When running in Azure add the vmrCloned health check
else
{
    app.MapHealthChecks("/vmrReady", new HealthCheckOptions
    {
        Predicate = healthCheck => healthCheck.Tags.Contains(VmrConfiguration.VmrReadyHealthCheckTag)
    });
}

app.Run();
