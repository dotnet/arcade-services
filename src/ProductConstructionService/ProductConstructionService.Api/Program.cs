// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.VirtualMonoRepo;

var builder = WebApplication.CreateBuilder(args);

string vmrPath = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrPathKey);
string tmpPath = builder.Configuration.GetRequiredValue(VmrConfiguration.TmpPathKey);
string vmrUri = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrUriKey);

DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration[PcsConfiguration.ManagedIdentityId]
});

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

builder.ConfigurePcs(
    vmrPath: vmrPath,
    tmpPath: tmpPath,
    vmrUri: vmrUri,
    azureCredential: credential,
    keyVaultUri: new Uri($"https://{builder.Configuration.GetRequiredValue(PcsConfiguration.KeyVaultName)}.vault.azure.net/"),
    initializeService: !isDevelopment,
    addEndpointAuthentication: !isDevelopment,
    addSwagger: useSwagger);

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
app.UseHttpLogging();
app.UseAuthorization();
app.MapControllers();

if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

// When running locally, create the workitem queue, if it doesn't already exist
// and add swaggerUI
if (isDevelopment)
{
    var queueServiceClient = app.Services.GetRequiredService<QueueServiceClient>();
    var queueClient = queueServiceClient.GetQueueClient(app.Configuration.GetRequiredValue(QueueConfiguration.JobQueueNameConfigurationKey));
    await queueClient.CreateIfNotExistsAsync();

    if (useSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI(); // UseSwaggerUI Protected by if (env.IsDevelopment())
    }
}

app.Run();
