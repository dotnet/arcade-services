// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Reads and writes the settings file
/// </summary>
internal class LocalSettings
{
    public string GitHubToken { get; set; }

    public string AzureDevOpsToken { get; set; }

    public string BuildAssetRegistryBaseUri { get; set; } = ProductConstructionServiceApiOptions.ProductionMaestroUri;

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
        catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException)
        {
            // User has not called darc authenticate yet
            // Not a problem of it self unless the operation they run needs the GitHub token
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

        localSettings.AzureDevOpsToken = PreferOptionToSetting(options.AzureDevOpsPat, localSettings.AzureDevOpsToken);
        localSettings.GitHubToken = PreferOptionToSetting(options.GitHubPat, localSettings.GitHubToken);
        localSettings.BuildAssetRegistryBaseUri = options.BuildAssetRegistryBaseUri
            ?? localSettings.BuildAssetRegistryBaseUri
            ?? ProductConstructionServiceApiOptions.ProductionMaestroUri;

        return localSettings;
    }
}
