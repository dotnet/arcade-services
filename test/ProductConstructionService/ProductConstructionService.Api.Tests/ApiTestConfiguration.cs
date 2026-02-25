// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Tests;

public static class ApiTestConfiguration
{
    public static WebApplicationBuilder CreateTestHostBuilder()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=value1");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["VmrPath"] = "vmrPath";
        builder.Configuration["TmpPath"] = "tmpPath";
        builder.Configuration["VmrUri"] = "https://vmr.com/uri";
        builder.Configuration[PcsStartup.ConfigurationKeys.DatabaseConnectionString] = "connectionString";
        builder.Configuration[PcsStartup.ConfigurationKeys.RedisConnectionString] = "connectionString";
        builder.Configuration[DataProtection.DataProtectionKeyUri] = "https://keyvault.azure.com/secret/key";
        builder.Configuration[DataProtection.DataProtectionKeyBlobUri] = "https://blobs.azure.com/secret/key";
        return builder;
    }
}
