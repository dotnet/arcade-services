// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Reads and writes the settings file
/// </summary>
internal class LocalSettings
{
    public string BuildAssetRegistryToken { get; set; }

    public string GitHubToken { get; set; }

    public string AzureDevOpsToken { get; set; }

    public string BuildAssetRegistryBaseUri { get; set; } = MaestroApi.ProductionBuildAssetRegistryBaseUri;

    /// <summary>
    /// Saves the settings in the settings files
    /// </summary>
    public int SaveSettingsFile(ILogger logger)
    {
        string settings = JsonConvert.SerializeObject(this);
        return EncodedFile.Create(Constants.SettingsFileName, settings, logger);
    }

    public static LocalSettings LoadSettingsFile()
    {
        string settings = EncodedFile.Read(Constants.SettingsFileName);
        return JsonConvert.DeserializeObject<LocalSettings>(settings);
    }

    public static LocalSettings LoadSettingsFile(ICommandLineOptions options)
    {
        try
        {
            var settings = LoadSettingsFile();
            settings.GitHubToken = options.GitHubPat ?? settings.GitHubToken;
            settings.AzureDevOpsToken = options.AzureDevOpsPat ?? settings.AzureDevOpsToken;
            settings.BuildAssetRegistryToken = options.BuildAssetRegistryToken ?? settings.BuildAssetRegistryToken;
            return settings;
        }
        catch (Exception exc) when (exc is DirectoryNotFoundException || exc is FileNotFoundException)
        {
            if (string.IsNullOrEmpty(options.AzureDevOpsPat) && string.IsNullOrEmpty(options.GitHubPat))
            {
                throw new DarcException("Please make sure to run darc authenticate and set" +
                                        " 'github_token' or 'azure_devops_token' or append" +
                                        "'-p <bar_token>' [--github-pat <github_token> | " +
                                        "--azdev-pat <azure_devops_token>] to your command");
            }
        }

        return null;
    }

    /// <summary>
    /// Retrieve the settings from the combination of the command line
    /// options and the user's darc settings file.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Darc settings for use in remote commands</returns>
    /// <remarks>The command line takes precedence over the darc settings file.</remarks>
    public static LocalSettings GetSettings(ICommandLineOptions options, ILogger logger, string repoUri = null)
    {
        LocalSettings localSettings = null;

        try
        {
            localSettings = LoadSettingsFile(options);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, $"Failed to load the darc settings file, may be corrupted");
        }

        localSettings ??= new LocalSettings();
        localSettings.BuildAssetRegistryToken = options.BuildAssetRegistryToken ?? localSettings.BuildAssetRegistryToken;
        localSettings.BuildAssetRegistryBaseUri = options.BuildAssetRegistryBaseUri
            ?? localSettings.BuildAssetRegistryBaseUri
            ?? MaestroApi.ProductionBuildAssetRegistryBaseUri;

        return localSettings;
    }
}
