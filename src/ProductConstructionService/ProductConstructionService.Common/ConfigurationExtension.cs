// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;

namespace ProductConstructionService.Common;
public static class ConfigurationExtension
{
    // In the PCS API, environment is set with the ASPNET_ENVIRONMENT variable
    // In other services, like the SubscriptionTriggerer, it is set with the DOTNET_ENVIRONMENT variable
    public static string GetRequiredValue(this IConfiguration config, string key) =>
        config[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");
}
