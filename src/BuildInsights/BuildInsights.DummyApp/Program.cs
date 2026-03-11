// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Maestro.Common.Cache;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

string? managedIdentityId = builder.Configuration["ManagedIdentityClientId"];

// SQL Server
builder.AddSqlServerClient("bi-mssql");

// Redis
string redisConnectionString = builder.Configuration.GetConnectionString("bi-redis")!;
await builder.AddRedisCache(redisConnectionString, managedIdentityId);

// Blob Storage
builder.AddAzureBlobServiceClient("bi-blobs", settings =>
{
    if (!string.IsNullOrEmpty(managedIdentityId))
    {
        settings.Credential = new ManagedIdentityCredential(managedIdentityId);
    }
});

// Key Vault
string? keyVaultName = builder.Configuration["KeyVaultName"];
if (!string.IsNullOrEmpty(keyVaultName))
{
    var kvUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
    var credential = !string.IsNullOrEmpty(managedIdentityId)
        ? new ManagedIdentityCredential(managedIdentityId)
        : new ManagedIdentityCredential();
    builder.Services.AddSingleton(new SecretClient(kvUri, credential));
}


var app = builder.Build();

app.MapControllers();

app.Run();
