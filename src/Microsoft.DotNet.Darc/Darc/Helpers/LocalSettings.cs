// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Reads and writes the settings file
/// </summary>
internal class LocalSettings
{
    private static readonly string _defaultBuildAssetRegistryBaseUri = "https://maestro-prod.westus2.cloudapp.azure.com/";

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

    public static LocalSettings LoadSettingsFile(CommandLineOptions options)
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
    public static DarcSettings GetDarcSettings(CommandLineOptions options, ILogger logger, string repoUri = null)
    {
        LocalSettings localSettings = null;
        DarcSettings darcSettings = new DarcSettings
        {
            GitType = GitRepoType.None
        };

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

        if (!string.IsNullOrEmpty(repoUri))
        {
            darcSettings.GitType = GitRepoTypeParser.ParseFromUri(repoUri);
            switch (darcSettings.GitType)
            {
                case GitRepoType.GitHub:
                    darcSettings.GitRepoPersonalAccessToken = localSettings != null
                        ? (string.IsNullOrEmpty(localSettings.GitHubToken) ? options.GitHubPat : localSettings.GitHubToken)
                        : options.GitHubPat;
                    break;

                case GitRepoType.AzureDevOps:
                    darcSettings.GitRepoPersonalAccessToken = localSettings != null
                        ? (string.IsNullOrEmpty(localSettings.AzureDevOpsToken) ? options.AzureDevOpsPat : localSettings.AzureDevOpsToken)
                        : options.AzureDevOpsPat;
                    break;

                case GitRepoType.None:
                    Console.WriteLine(
                        $"Unknown repository '{repoUri}', repo type set to 'None'. " +
                        ((!repoUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            ? $"Please inform the full URL of the repository including the http[s] prefix."
                            : string.Empty));
                    break;
            }
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
