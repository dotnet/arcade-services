// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Queue;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    new DefaultAzureCredential());

builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
{
    options.UseSqlServer(builder.Configuration["build-asset-registry-sql-connection-string"] ?? string.Empty);
});

builder.AddWorkitemQueues();

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

app.Run();
