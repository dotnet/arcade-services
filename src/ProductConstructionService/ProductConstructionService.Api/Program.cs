// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.DotNet.Kusto;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.Controllers;
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

builder.AddBuildAssetRegistry(databaseConnectionString);
builder.AddTelemetry();
builder.AddVmrRegistrations(vmrPath, tmpPath);
builder.AddGitHubClientFactory();

// When not running locally, wait for the VMR to be initialized before starting the background processor
// and don't add authentication to the API endpoints
if (!builder.Environment.IsDevelopment())
{
    builder.AddVmrInitialization(vmrUri);
    builder.AddWorkitemQueues(credential, waitForInitialization: true);
    builder.AddEndpointAuthentication(requirePolicyRole: true);
}
else
{
    builder.AddWorkitemQueues(credential, waitForInitialization: false);
}

builder.Services.AddKustoClientProvider("Kusto");
builder.AddServiceDefaults();
builder.Services.AddControllers().EnableInternalControllers();
builder.ConfigureSwagger();

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
