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
            new("GitHub auth", isComment: true),
            new("===========", isComment: true),
            new("- (Recommended) Setup GH CLI (https://cli.github.com/manual/), sign in (gh auth login) and leave empty", isComment: true),
            new("- (Alternative) Create new GitHub personal access token at https://github.com/settings/personal-access-tokens", isComment: true),
            new("  - Choose `dotnet` as the resource owner", isComment: true),
            new("  - Turn on SSO for orgs where you need to access repositories in (typically dotnet and microsoft)", isComment: true),
            new("- (Not Recommended) Leave empty but rate limits are very low then", isComment: true),
            new($"{GithubTokenElement}={GetCurrentSettingForDisplay(settings.GitHubToken, string.Empty, true)}"),
            new(string.Empty),
            new(string.Empty),
            new("Azure DevOps auth", isComment: true),
            new("=================", isComment: true),
            new("- (Recommended) Leave empty and darc will sign you in via a browser or device code auth flow", isComment: true),
            new("- (Alternative) Create a PAT with the `Build.Execute` and `Code.Read` scopes", isComment: true),
            new("- (Alternative) Use the PatGeneratorTool https://dev.azure.com/dnceng/public/_artifacts/feed/dotnet-eng/NuGet/Microsoft.DncEng.PatGeneratorTool", isComment: true),
            new("  - Run `dotnet pat-generator --scopes build_execute code_write --organizations dnceng devdiv --expires-in 7`", isComment: true),
            new("  - Token lasts 7 days", isComment: true),
            new($"{AzureDevOpsTokenElement}={GetCurrentSettingForDisplay(settings.AzureDevOpsToken, string.Empty, true)}"),
            new(string.Empty),
            new(string.Empty),
            new("Maestro API", isComment: true),
            new("===========", isComment: true),
            new("- (Recommended) Leave as-is", isComment: true),
            new($"{BarBaseUriElement}={GetCurrentSettingForDisplay(settings.BuildAssetRegistryBaseUri, "<alternate Maestro API URI, otherwise leave as is>", false)}"),
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
