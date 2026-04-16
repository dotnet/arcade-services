// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.ReproTool.Operations;
using BuildInsights.ReproTool.Options;
using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

Type[] options =
[
    typeof(ReproOptions),
];

Parser.Default.ParseArguments(args, options)
    .MapResult(
        (Options o) =>
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddUserSecrets<ReproOperation>()
                .AddEnvironmentVariables()
                .Build();

            o.GitHubToken ??= configuration["GITHUB_TOKEN"];
            o.GitHubToken ??= GetGitHubTokenFromGhCli();
            ArgumentNullException.ThrowIfNull(o.GitHubToken, nameof(o.GitHubToken));

            o.AzDoHookSecret ??= configuration["AZDO_SERVICE_HOOK_SECRET"];
            o.AzDoHookSecret ??= configuration["BUILD_INSIGHTS_AZDO_SERVICE_HOOK_SECRET"];
            o.AzDoHookSecret ??= configuration["KeyVaultSecrets:azdo-service-hook-secret"];
            o.AzDoHookSecret ??= configuration["KeyVaultSecrets__azdo-service-hook-secret"];
            ArgumentNullException.ThrowIfNull(o.AzDoHookSecret, nameof(o.AzDoHookSecret));

            o.LocalBuildInsightsUrl ??= configuration["BUILD_INSIGHTS_API_BASE_URL"];

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
