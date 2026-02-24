// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace BuildInsights.Api.Tests;

public static class ApiTestConfiguration
{
    public static WebApplicationBuilder CreateTestHostBuilder()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Staging);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=value1");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["ManagedIdentityClientId"] = "test-managed-identity-client-id";
        builder.Configuration["ConnectionStrings:redis"] = "localhost:6379";
        builder.Configuration["WorkItemQueueName"] = "test-queue";
        builder.Configuration["SpecialWorkItemQueueName"] = "test-special-queue";
        builder.Configuration["WorkItemConsumerCount"] = "1";
        builder.Configuration["GitHubApp:AppId"] = "12345";
        builder.Configuration["DataProtection:DataProtectionKeyUri"] = "https://keyvault.azure.com/secret/key";
        builder.Configuration["DataProtection:KeyBlobUri"] = "https://blobs.azure.com/secret/key";
        return builder;
    }
}
