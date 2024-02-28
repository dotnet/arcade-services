// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;

var builder = WebApplication.CreateBuilder(args);

string vmrPath = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrPathKey);
string tmpPath = builder.Configuration.GetRequiredValue(VmrConfiguration.TmpPathKey);
string vmrUri = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrUriKey);

DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration[PcsConfiguration.ManagedIdentityId]
});

builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration.GetRequiredValue(PcsConfiguration.KeyVaultName)}.vault.azure.net/"),
    credential);

string databaseConnectionString = builder.Configuration.GetRequiredValue(PcsConfiguration.DatabaseConnectionString);

builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
{
    options.UseSqlServer(databaseConnectionString, sqlOptions =>
    {
        sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
    });
});

builder.AddTelemetry();

builder.AddVmrRegistrations(vmrPath, tmpPath, vmrUri);
builder.AddWorkitemQueues(credential);
builder.Services.AddKustoClientProvider("Kusto");

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
    var queueClient = queueServiceClient.GetQueueClient(app.Configuration.GetRequiredValue(QueueConfiguration.JobQueueNameConfigurationKey));
    await queueClient.CreateIfNotExistsAsync();
}

app.Run();
