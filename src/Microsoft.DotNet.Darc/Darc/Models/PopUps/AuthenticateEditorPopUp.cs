// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Models.PopUps;

internal class AuthenticateEditorPopUp : EditorPopUp
{
    private readonly ILogger _logger;

    private const string GithubTokenElement = "github_token";
    private const string AzureDevOpsTokenElement = "azure_devops_token";
    private const string BarBaseUriElement = "build_asset_registry_base_uri";

    public AuthenticateEditorPopUp(string path, ILogger logger)
        : base(path)
    {
        _logger = logger;

        try
        {
            // Load current settings
            settings = LocalSettings.LoadSettingsFile();
        }
        catch (Exception e)
        {
            // Failed to load the settings file.  Quite possible it just doesn't exist.
            // In this case, just initialize the settings to empty
            _logger.LogTrace($"Couldn't load or locate the settings file ({e.Message}). Initializing an empty settings file");
        }

        settings ??= new LocalSettings();

        // Initialize line contents.
        Contents =
        [
            new("Create new GitHub personal access tokens at https://github.com/settings/personal-access-tokens (choose 'dotnet' as the resource owner)", isComment: true),
            new($"{GithubTokenElement}={GetCurrentSettingForDisplay(settings.GitHubToken, string.Empty, true)}"),
            new(string.Empty),
            new("[OPTIONAL]", isComment: true),
            new("Set an Azure DevOps token (or leave empty to use local credentials)", isComment: true),
            new("Create an AzDO PAT with the Build.Execute and Code.Read scopes at ", isComment: true),
            new("Or use the PatGeneratorTool https://dev.azure.com/dnceng/public/_artifacts/feed/dotnet-eng/NuGet/Microsoft.DncEng.PatGeneratorTool", isComment: true),
            new("with the `dotnet pat-generator --scopes build_execute code --organizations dnceng devdiv --expires-in 7` command", isComment: true),
            new($"{AzureDevOpsTokenElement}={GetCurrentSettingForDisplay(settings.AzureDevOpsToken, string.Empty, true)}"),
            new(string.Empty),
            new($"{BarBaseUriElement}={GetCurrentSettingForDisplay(settings.BuildAssetRegistryBaseUri, "<alternate build asset registry uri if needed, otherwise leave as is>", false)}"),
            new(string.Empty),
            new($"Set elements above depending on what you need", true),
        ];
    }

    public LocalSettings settings { get; set; }

    public override Task<int> ProcessContents(IList<Line> contents)
    {
        foreach (Line line in contents)
        {
            var keyValue = line.Text.Split("=");

            switch (keyValue[0])
            {
                case GithubTokenElement:
                    settings.GitHubToken = ParseSetting(keyValue[1], settings.GitHubToken, true);
                    break;
                case AzureDevOpsTokenElement:
                    settings.AzureDevOpsToken = ParseSetting(keyValue[1], settings.AzureDevOpsToken, true);
                    break;
                case BarBaseUriElement:
                    settings.BuildAssetRegistryBaseUri = ParseSetting(keyValue[1], settings.BuildAssetRegistryBaseUri, false);
                    break;
                default:
                    _logger.LogWarning($"'{keyValue[0]}' is an unknown field in the authentication scope");
                    break;
            }
        }

        return Task.FromResult(settings.SaveSettingsFile(_logger));
    }
}
