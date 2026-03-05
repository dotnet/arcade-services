// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BuildInsights.ServiceDefaults;

public static class SharedConfigurationExtensions
{
    private const string SharedSettingsPrefix = "appsettings.Shared";

    /// <summary>
    /// Adds shared appsettings files from the BuildInsights root directory to the configuration.
    /// These files are inserted before the project-specific appsettings files so that
    /// project-specific settings take precedence over shared settings.
    /// </summary>
    /// <remarks>
    /// During development, the shared files are resolved from the parent of the content root
    /// (i.e., src/BuildInsights/ relative to src/BuildInsights/BuildInsights.Api/).
    /// For published apps, the shared files should be copied to the output via MSBuild
    /// (see SharedConfiguration.targets) and are resolved from the content root directly.
    /// </remarks>
    public static TBuilder AddSharedConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        string sharedConfigDirectory = ResolveSharedConfigDirectory(builder.Environment.ContentRootPath);

        if (!Directory.Exists(sharedConfigDirectory))
        {
            return builder;
        }

        AddSharedConfiguration(
            builder.Configuration,
            sharedConfigDirectory,
            builder.Environment.EnvironmentName);

        return builder;
    }

    public static void AddSharedConfiguration(
        this IConfigurationManager configuration,
        string configDirectory,
        string environmentName)
    {
        var sharedFileProvider = new PhysicalFileProvider(configDirectory);

        // Create the shared configuration sources to insert
        var sharedSources = new List<JsonConfigurationSource>
        {
            new()
            {
                FileProvider = sharedFileProvider,
                Path = $"{SharedSettingsPrefix}.json",
                Optional = true,
                ReloadOnChange = true,
            },
            new()
            {
                FileProvider = sharedFileProvider,
                Path = $"{SharedSettingsPrefix}.{environmentName}.json",
                Optional = true,
                ReloadOnChange = true,
            },
        };

        // Insert the shared sources at the beginning of the configuration sources list
        // so that project-specific appsettings files (added by the default builder) take precedence
        for (int i = 0; i < sharedSources.Count; i++)
        {
            configuration.Sources.Insert(i, sharedSources[i]);
        }
    }

    /// <summary>
    /// Resolves the directory containing shared configuration files.
    /// Checks the content root first (for published apps), then the parent directory (for development).
    /// </summary>
    private static string ResolveSharedConfigDirectory(string contentRoot)
    {
        // For published apps, the shared files are copied next to the project-specific ones
        string sharedFileInContentRoot = Path.Combine(contentRoot, $"{SharedSettingsPrefix}.json");
        if (File.Exists(sharedFileInContentRoot))
        {
            return contentRoot;
        }

        // During development, shared files live in the parent directory (src/BuildInsights/)
        return Path.GetFullPath(Path.Combine(contentRoot, ".."));
    }
}
