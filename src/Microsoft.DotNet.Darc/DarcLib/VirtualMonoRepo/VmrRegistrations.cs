// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public static class VmrRegistrations
{
    // This one is used in the context of the PCS service
    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string tmpPath,
        string gitLocation = "git")
    {
        // TODO: Configure this somehow
        services.TryAddScoped<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        return AddVmrManagers(services, gitLocation, null, null);
    }

    // This one is used in the context of darc and E2E tests
    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        string vmrPath,
        string tmpPath,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        return AddVmrManagers(services, gitLocation, gitHubToken, azureDevOpsToken);
    }

    private static IServiceCollection AddVmrManagers(
        IServiceCollection services,
        string gitLocation,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        // Configuration based registrations
        services.TryAddSingleton<IRemoteTokenProvider>(sp =>
        {
            if (!string.IsNullOrEmpty(azureDevOpsToken))
            {
                return new RemoteTokenProvider(azureDevOpsToken, gitHubToken);
            }

            var azdoTokenProvider = sp.GetRequiredService<IAzureDevOpsTokenProvider>();
            return new RemoteTokenProvider(azdoTokenProvider, gitHubToken);
        });

        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<VmrManagerBase>>());
        services.TryAddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), gitLocation));
        services.TryAddTransient<IDependencyFileManager, DependencyFileManager>();
        services.TryAddTransient<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, sp.GetRequiredService<IVmrInfo>().TmpPath.ToString()));
        services.TryAddTransient<ILocalGitRepoFactory, LocalGitRepoFactory>();
        services.TryAddTransient<ILocalGitClient, LocalGitClient>();
        services.TryAddTransient<ILocalLibGit2Client, LocalLibGit2Client>();
        services.TryAddTransient<IAzureDevOpsClient, AzureDevOpsClient>();
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddTransient<IVmrPatchHandler, VmrPatchHandler>();
        services.TryAddTransient<IVmrUpdater, VmrUpdater>();
        services.TryAddTransient<IVmrInitializer, VmrInitializer>();
        services.TryAddTransient<IVmrBackFlower, VmrBackFlower>();
        services.TryAddTransient<IPcsVmrBackFlower, PcsVmrBackFlower>();
        services.TryAddTransient<IVmrForwardFlower, VmrForwardFlower>();
        services.TryAddTransient<IPcsVmrForwardFlower, PcsVmrForwardFlower>();
        services.TryAddTransient<IVmrRepoVersionResolver, VmrRepoVersionResolver>();
        services.TryAddSingleton<IVmrDependencyTracker, VmrDependencyTracker>();
        services.TryAddTransient<IWorkBranchFactory, WorkBranchFactory>();
        services.TryAddTransient<IThirdPartyNoticesGenerator, ThirdPartyNoticesGenerator>();
        services.TryAddTransient<IComponentListGenerator, ComponentListGenerator>();
        services.TryAddTransient<ICodeownersGenerator, CodeownersGenerator>();
        services.TryAddTransient<ICredScanSuppressionsGenerator, CredScanSuppressionsGenerator>();
        services.TryAddTransient<IFileSystem, FileSystem>();
        services.TryAddTransient<IGitRepoCloner, GitNativeRepoCloner>();
        services.TryAddTransient<VmrCloakedFileScanner>();
        services.TryAddTransient<IDependencyFileManager, DependencyFileManager>();
        services.TryAddTransient<ICoherencyUpdateResolver, CoherencyUpdateResolver>();
        services.TryAddTransient<IAssetLocationResolver, AssetLocationResolver>();
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddScoped<IVmrCloneManager, VmrCloneManager>();
        services.TryAddScoped<IRepositoryCloneManager, RepositoryCloneManager>();
        services.TryAddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();

        services.AddHttpClient("GraphQL", httpClient =>
        {
            httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, "application/json");
            httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, "Darc");
        }).ConfigurePrimaryHttpMessageHandler((handler, service) =>
        {
            if (handler is HttpClientHandler httpClientHandler)
            {
                httpClientHandler.CheckCertificateRevocationList = true;
            }
            else if (handler is SocketsHttpHandler socketsHttpHandler)
            {
                socketsHttpHandler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.Online;
            }
            else
            {
                throw new InvalidOperationException($"Could not create client with CRL check, HttpMessageHandler type {handler.GetType().FullName ?? handler.GetType().Name} is unknown.");
            }
        });

        services.TryAddTransient<IVmrPusher, VmrPusher>();

        // These initialize the configuration by reading the JSON files in VMR's src/
        services.TryAddSingleton<ISourceManifest>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrInfo>();
            return SourceManifest.FromJson(configuration.SourceManifestPath);
        });

        return services;
    }
}
