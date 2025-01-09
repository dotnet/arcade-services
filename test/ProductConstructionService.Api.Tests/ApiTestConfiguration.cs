// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace ProductConstructionService.Api.Tests;

public static class ApiTestConfiguration
{
    public static WebApplicationBuilder CreateTestHostBuilder()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Staging);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=value1");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["VmrPath"] = "vmrPath";
        builder.Configuration["TmpPath"] = "tmpPath";
        builder.Configuration["VmrUri"] = "https://vmr.com/uri";
        builder.Configuration["BuildAssetRegistrySqlConnectionString"] = "connectionString";
        builder.Configuration["DataProtection:DataProtectionKeyUri"] = "https://keyvault.azure.com/secret/key";
        builder.Configuration["DataProtection:KeyBlobUri"] = "https://blobs.azure.com/secret/key";
        return builder;
    }
}
