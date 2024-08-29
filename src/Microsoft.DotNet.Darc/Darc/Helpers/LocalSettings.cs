// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.Darc.Options;
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

    // Old way of storing the settings had the password and not the token so we keep both to deserialize these correctly.
    public string BuildAssetRegistryPassword { get; set; }

    public string GitHubToken { get; set; }

    public string AzureDevOpsToken { get; set; }

    public string BuildAssetRegistryBaseUri { get; set; } = MaestroApiOptions.ProductionMaestroUri;

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

    /// <summary>
    /// Retrieve the settings from the combination of the command line
    /// options and the user's darc settings file.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Darc settings for use in remote commands</returns>
    /// <remarks>The command line takes precedence over the darc settings file.</remarks>
    public static LocalSettings GetSettings(ICommandLineOptions options, ILogger logger)
    {
        LocalSettings localSettings = null;

        try
        {
            localSettings = LoadSettingsFile();
        }
        catch (Exception e)
        {
            if (!options.IsCi && options.OutputFormat != DarcOutputType.json)
            {
                logger.LogInformation(e, $"Failed to load the darc settings file, may be corrupted");
            }
        }

        static string PreferOptionToSetting(string option, string localSetting)
        {
            return !string.IsNullOrEmpty(option) ? option : localSetting;
        }

        // Prefer the command line options over the settings file
        localSettings ??= new LocalSettings();

        if (string.IsNullOrEmpty(localSettings.BuildAssetRegistryToken))
        {
            // Old way of storing the settings had the password and not the token
            localSettings.BuildAssetRegistryToken = localSettings.BuildAssetRegistryPassword;
        }

        localSettings.AzureDevOpsToken = PreferOptionToSetting(options.AzureDevOpsPat, localSettings.AzureDevOpsToken);
        localSettings.GitHubToken = PreferOptionToSetting(options.GitHubPat, localSettings.GitHubToken);
        localSettings.BuildAssetRegistryToken = PreferOptionToSetting(options.BuildAssetRegistryToken, localSettings.BuildAssetRegistryToken);
        localSettings.BuildAssetRegistryBaseUri = options.BuildAssetRegistryBaseUri
            ?? localSettings.BuildAssetRegistryBaseUri
            ?? MaestroApiOptions.ProductionMaestroUri;

        return localSettings;
    }
}
