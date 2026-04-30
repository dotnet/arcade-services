// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Microsoft.DotNet.Darc;

internal static class DarcVersionValidator
{
    private const string DevVersionSuffix = "-dev";

    public static async Task<bool> ValidateAsync(IProductConstructionServiceApi pcsClient, ILogger logger)
    {
        var darcVersionString = GetDarcVersion();

        // Dev builds bypass enforcement, no need to call the service.
        if (string.IsNullOrEmpty(darcVersionString)
            || darcVersionString.EndsWith(DevVersionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string minVersionString;
        try
        {
            minVersionString = await pcsClient.MinDarcVersion.GetMinDarcVersionAsync();
        }
        catch (Exception ex)
        {
            // No minimum configured / failed to read it -> pass through (matches server-side behavior).
            logger.LogInformation(ex, "Failed to read the minimum darc client version from the Product Construction Service.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(minVersionString))
        {
            return true;
        }

        if (!NuGetVersion.TryParse(minVersionString, out var minVersion))
        {
            logger.LogInformation("Minimum darc client version reported by the Product Construction Service ('{MinVersion}') is not a valid version. Skipping enforcement.", minVersionString);
            return true;
        }

        if (!NuGetVersion.TryParse(darcVersionString, out var darcVersion))
        {
            logger.LogError("Current darc version '{DarcVersion}' could not be parsed. Please upgrade your darc client.", darcVersionString);
            return false;
        }

        if (darcVersion < minVersion)
        {
            logger.LogError(
                "Your darc version {DarcVersion} is below the minimum required version {MinVersion}. Run `darc-init` (or `dotnet tool update -g microsoft.dotnet.darc`) to upgrade.",
                darcVersion.ToNormalizedString(),
                minVersion.ToNormalizedString());
            return false;
        }

        return true;
    }

    private static string GetDarcVersion()
    {
        string informationalVersion = typeof(DarcVersionValidator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Strip the SourceLink "+<commit-sha>" suffix
        int plusIndex = informationalVersion?.IndexOf('+') ?? -1;
        return plusIndex >= 0 ? informationalVersion.Substring(0, plusIndex) : informationalVersion;
    }
}
