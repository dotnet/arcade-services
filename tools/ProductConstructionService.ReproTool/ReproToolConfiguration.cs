// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Maestro.DataProviders;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ProductConstructionService.Client;
using GitHubClient = Octokit.GitHubClient;
using Octokit;
using Microsoft.Extensions.Logging.Console;

namespace ProductConstructionService.ReproTool;
internal static class ReproToolConfiguration
{
    private const string LocalDbConnectionString = "Data Source=localhost\\SQLEXPRESS;Initial Catalog=BuildAssetRegistry;Integrated Security=true";
    private const string MaestroProdUri = "https://maestro.dot.net";
    internal const string PcsLocalUri = "https://localhost:53180";

    internal static ServiceCollection RegisterServices(
        this ServiceCollection services,
        ReproToolOptions options)
    {
        services.AddSingleton(options);
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<IProcessManager>>());

        services.AddSingleton<IBarApiClient>(sp => new BarApiClient(
            null,
            managedIdentityId: null,
            disableInteractiveAuth: false,
            MaestroProdUri));
        services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        services.AddSingleton<DarcProcessManager>();
        services.AddKeyedSingleton("local", PcsApiFactory.GetAnonymous(PcsLocalUri));
        services.AddKeyedSingleton("prod", PcsApiFactory.GetAuthenticated("https://maestro.dot.net/", null, null, false));
        services.AddSingleton(_ => new GitHubClient(new ProductHeaderValue("repro-tool"))
        {
            Credentials = new Credentials(options.GitHubToken)
        });

        services.TryAddTransient<IBasicBarClient, SqlBarClient>();

        services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            // Do not log DB context initialization and command executed events
            options.ConfigureWarnings(w =>
            {
                w.Ignore(CoreEventId.ContextInitialized);
                w.Ignore(RelationalEventId.CommandExecuted);
            });

            options.UseSqlServer(LocalDbConnectionString, sqlOptions =>
            {
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            });
        });

        services.AddKustoClientProvider("Kusto");
        services.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();

        return services;
    }
}
