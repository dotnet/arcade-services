// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;

var builder = WebApplication.CreateBuilder(args);

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
builder.AddWorkitemQueues(credential);

builder.AddVmrRegistrations();

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

var vmr = app.Services.GetRequiredService<IRepositoryCloneManager>();
await vmr.PrepareCloneAsync("https://github.com/dotnet/dotnet", "17a7bb483ced4ad57c400d96e88048ec6221ef3d", CancellationToken.None);

app.Run();
