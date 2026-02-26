// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;

namespace Maestro.Common;

public static class ConfigurationExtension
{
    public static string GetRequiredValue(this IConfiguration config, string key) =>
        config[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");
}
