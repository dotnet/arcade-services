// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace BuildInsights.Api.Tests;

public static class ApiTestConfiguration
{
    public static WebApplicationBuilder CreateTestHostBuilder()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=value1");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["ConnectionStrings:redis"] = "localhost:6379";

        builder.Configuration[BuildInsightsStartup.ConfigurationKeys.DatabaseConnectionString] = "sql:3555";
        builder.Configuration[BuildInsightsStartup.ConfigurationKeys.RedisConnectionString] = "localhost:6379";
        builder.Configuration[DataProtection.DataProtectionKeyUri] = "https://keyvault.azure.com/secret/key";
        builder.Configuration[DataProtection.DataProtectionKeyBlobUri] = "https://blobs.azure.com/secret/key";
        builder.Configuration[BuildInsightsStartup.ConfigurationKeys.WorkItemQueueName] = "test-queue";
        builder.Configuration[BuildInsightsStartup.ConfigurationKeys.SpecialWorkItemQueueName] = "test-special-queue";
        builder.Configuration[BuildInsightsStartup.ConfigurationKeys.WorkItemConsumerCount] = "1";
        builder.Configuration[$"{BuildInsightsStartup.ConfigurationKeys.GitHubApp}:AppId"] = "12345";
        return builder;
    }
}
