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
    ///     If the git clients need to clone a repository for whatever reason,
    ///     this denotes the root of where the repository should be cloned.
    /// </summary>
    public string TemporaryRepositoryRoot { get; set; }

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
            return settings;
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
    public static LocalSettings GetSettings(ICommandLineOptions options, ILogger logger, string repoUri = null)
    {
        LocalSettings localSettings;

        try
        {
            localSettings = LoadSettingsFile(options);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, $"Failed to load the darc settings file, may be corrupted");
            localSettings = new LocalSettings();
        }

        localSettings.BuildAssetRegistryPassword = options.BuildAssetRegistryPassword ?? localSettings.BuildAssetRegistryPassword;
        localSettings.BuildAssetRegistryBaseUri = options.BuildAssetRegistryBaseUri
            ?? localSettings.BuildAssetRegistryBaseUri
            ?? _defaultBuildAssetRegistryBaseUri;

        return localSettings;
    }
}
