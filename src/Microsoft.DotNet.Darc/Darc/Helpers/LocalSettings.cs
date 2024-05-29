// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Reads and writes the settings file
/// </summary>
internal class LocalSettings
{
    private static readonly string _defaultBuildAssetRegistryBaseUri = "https://maestro.dot.net/";

    public string BuildAssetRegistryPassword { get; set; }

    public string GitHubToken { get; set; }

    public string AzureDevOpsToken { get; set; }

    public string BuildAssetRegistryBaseUri { get; set; } = _defaultBuildAssetRegistryBaseUri;

    /// <summary>
    /// Saves the settings in the settings files
    /// </summary>
    /// <param name="logger"></param>
    /// <returns></returns>
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
            return LoadSettingsFile();
        }
        catch (Exception exc) when (exc is DirectoryNotFoundException || exc is FileNotFoundException)
        {
            if (string.IsNullOrEmpty(options.AzureDevOpsPat) &&
                string.IsNullOrEmpty(options.GitHubPat) &&
                string.IsNullOrEmpty(options.BuildAssetRegistryPassword))
            {
                throw new DarcException("Please make sure to run darc authenticate and set" +
                                        " 'bar_password' and 'github_token' or 'azure_devops_token' or append" +
                                        "'-p <bar_password>' [--github-pat <github_token> | " +
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
    public static DarcSettings GetDarcSettings(ICommandLineOptions options, ILogger logger, string repoUri = null)
    {
        LocalSettings localSettings = null;
        var darcSettings = new DarcSettings();

        try
        {
            localSettings = LoadSettingsFile(options);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, $"Failed to load the darc settings file, may be corrupted");
        }

        if (localSettings != null)
        {
            darcSettings.BuildAssetRegistryBaseUri = localSettings.BuildAssetRegistryBaseUri;
            darcSettings.BuildAssetRegistryPassword = localSettings.BuildAssetRegistryPassword;
        }
        else
        {
            darcSettings.BuildAssetRegistryBaseUri = _defaultBuildAssetRegistryBaseUri;
            darcSettings.BuildAssetRegistryPassword = options.BuildAssetRegistryPassword;
        }

        // Override if non-empty on command line
        darcSettings.BuildAssetRegistryBaseUri = OverrideIfSet(darcSettings.BuildAssetRegistryBaseUri,
            options.BuildAssetRegistryBaseUri);
        darcSettings.BuildAssetRegistryPassword = OverrideIfSet(darcSettings.BuildAssetRegistryPassword,
            options.BuildAssetRegistryPassword);

        return darcSettings;
    }

    private static string OverrideIfSet(string currentSetting, string commandLineSetting)
    {
        return !string.IsNullOrEmpty(commandLineSetting) ? commandLineSetting : currentSetting;
    }
}
