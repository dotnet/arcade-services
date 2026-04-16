// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using BuildInsights.ReproTool.Options;
using BuildInsights.ServiceDefaults;
using CommandLine;
using Maestro.Services.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

Type[] options =
[
    typeof(ReproOptions),
];

Parser.Default.ParseArguments(args, options)
    .MapResult(
        (Options o) =>
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
            });
            builder.AddSharedConfiguration();

            string keyVaultName = builder.Configuration[BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultName]
                ?? throw new InvalidOperationException("KeyVaultName is not configured.");

            builder.Configuration.AddAzureKeyVault(
                new Uri($"https://{keyVaultName}.vault.azure.net/"),
                new DefaultAzureCredential(),
                new KeyVaultSecretsWithPrefix(BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultSecretPrefix));

            o.GitHubToken ??= GetGitHubTokenFromGhCli();
            o.GitHubToken ??= builder.Configuration["GITHUB_TOKEN"];
            ArgumentNullException.ThrowIfNull(o.GitHubToken, nameof(o.GitHubToken));

            o.AzDoHookSecret ??= builder.Configuration[$"{BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultSecretPrefix}azdo-service-hook-secret"];
            ArgumentNullException.ThrowIfNull(o.AzDoHookSecret, nameof(o.AzDoHookSecret));

            o.LocalBuildInsightsUrl ??= builder.Configuration["BUILD_INSIGHTS_API_BASE_URL"];

            var services = new ServiceCollection();
            o.RegisterServices(services);

            IServiceProvider provider = services.BuildServiceProvider();
            o.GetOperation(provider).RunAsync().GetAwaiter().GetResult();

            return 0;
        },
        _ => -1);

static string? GetGitHubTokenFromGhCli()
{
    try
    {
        var processManager = new ProcessManager(NullLogger.Instance, "git");
        ProcessExecutionResult result = processManager.Execute("gh", ["auth", "token"], TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        return null;
    }
    catch
    {
        return null;
    }
}
