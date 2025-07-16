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
    public static IServiceCollection AddMultiVmrSupport(
        this IServiceCollection services,
        string tmpPath,
        string gitLocation = "git")
    {
        tmpPath = Path.GetFullPath(tmpPath);

        // When running in the context of the PCS service, we don't have one VMR path
        // We will always set the VmrPath whenever we call VmrCloneManager to prepare a specific VMR for us
        // Then we will initialize VmrInfo and SourceManifest data from that path
        // We assume only one VMR will be within a DI scope (one per background job basically)
        services.TryAddScoped<IVmrInfo>(sp => new VmrInfo(string.Empty, tmpPath));
        services.TryAddScoped<ISourceManifest>(sp => new SourceManifest([], []));

        return AddVmrManagers(services, gitLocation, null, null);
    }

    // This one is used in the context of darc and E2E tests
    public static IServiceCollection AddSingleVmrSupport(
        this IServiceCollection services,
        string gitLocation,
        string vmrPath,
        string tmpPath,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        // When running in the context of darc or E2E tests, we have one VMR path for the whole lifetime of the process
        // We can statically initialize the information right away
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        services.TryAddScoped<ISourceManifest>(sp =>
        {
            var vmrInfo = sp.GetRequiredService<IVmrInfo>();
            return SourceManifest.FromFile(vmrInfo.SourceManifestPath);
        });

        return AddVmrManagers(services, gitLocation, gitHubToken, azureDevOpsToken);
    }

    /// <summary>
    /// Registers dependencies for GitNativeRepoCloner.
    /// It only supports Azure DevOps connections and it assumes that a dependency on IAzureDevOpsTokenProvider has been registered.
    /// </summary>
    public static IServiceCollection AddGitNativeRepoClonerSupport(this IServiceCollection services)
    {
        services.TryAddTransient<IGitRepoCloner, GitNativeRepoCloner>();
        services.TryAddSingleton<IRemoteTokenProvider>(sp =>
        {
            var azdoTokenProvider = sp.GetRequiredService<IAzureDevOpsTokenProvider>();
            return new RemoteTokenProvider(azdoTokenProvider, null);
        });
        services.TryAddTransient<ILocalGitClient, LocalGitClient>();
        services.TryAddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), "git"));
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<VmrManagerBase>>());
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddTransient<IFileSystem, FileSystem>();

        return services;
    }

    private static IServiceCollection AddVmrManagers(
        IServiceCollection services,
        string gitLocation,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        // Configuration based registrations
        services.TryAddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
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
        services.TryAddTransient<IVmrForwardFlower, VmrForwardFlower>();
        services.TryAddTransient<ICodeFlowVmrUpdater, CodeFlowVmrUpdater>();
        services.TryAddTransient<IBackflowConflictResolver, BackflowConflictResolver>();
        services.TryAddTransient<IForwardFlowConflictResolver, ForwardFlowConflictResolver>();
        services.TryAddTransient<IVmrVersionFileMerger, VmrVersionFileMerger>();
        services.TryAddTransient<ICodeflowChangeAnalyzer, CodeflowChangeAnalyzer>();
        services.TryAddTransient<IWorkBranchFactory, WorkBranchFactory>();
        services.TryAddTransient<IThirdPartyNoticesGenerator, ThirdPartyNoticesGenerator>();
        services.TryAddTransient<ICodeownersGenerator, CodeownersGenerator>();
        services.TryAddTransient<ICredScanSuppressionsGenerator, CredScanSuppressionsGenerator>();
        services.TryAddTransient<IFileSystem, FileSystem>();
        services.TryAddTransient<IGitRepoCloner, GitNativeRepoCloner>();
        services.TryAddTransient<VmrCloakedFileScanner>();
        services.TryAddTransient<IVmrPusher, VmrPusher>();
        services.TryAddTransient<IDependencyFileManager, DependencyFileManager>();
        services.TryAddTransient<ICoherencyUpdateResolver, CoherencyUpdateResolver>();
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddScoped<IVmrCloneManager, VmrCloneManager>();
        services.TryAddScoped<IRepositoryCloneManager, RepositoryCloneManager>();
        services.TryAddScoped<IVmrDependencyTracker, VmrDependencyTracker>();
        services.TryAddScoped<IAssetLocationResolver, AssetLocationResolver>();

        services
            .AddHttpClient("GraphQL", httpClient =>
            {
                httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, "application/json");
                httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, "Darc");
            })
            .ConfigurePrimaryHttpMessageHandler((handler, service) =>
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

        return services;
    }
}
