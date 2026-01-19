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

public static class CodeflowExtensions
{
    /// <summary>
    /// Registers classes required to perform VMR codeflow.
    /// </summary>
    /// <param name="vmrPath">A static path to a known VMR clone (e.g. in darc). If null, VmrCloneManager must be used to obtain a clone later.</param>
    /// <param name="tmpPath">The path to the temporary directory used by Codeflow for repo clones / intermediate files. Static path means darc will be able to reuse existing clones.</param>
    public static IServiceCollection AddCodeflow(
        this IServiceCollection services,
        string tmpPath,
        string? vmrPath = null,
        string gitLocation = "git")
    {
        services.TryAddScoped<IVmrInfo>(_ => new VmrInfo(
            vmrPath == null ? string.Empty : Path.GetFullPath(vmrPath),
            Path.GetFullPath(tmpPath)));
        services.TryAddScoped<ISourceManifest>(sp =>
        {
            var vmrInfo = sp.GetRequiredService<IVmrInfo>();
            if (vmrInfo.VmrPath == string.Empty)
            {
                return new SourceManifest([], []);
            }
            return SourceManifest.FromFile(vmrInfo.SourceManifestPath);
        });

        services.TryAddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
        services.TryAddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();

        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<VmrManagerBase>>());
        services.TryAddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), gitLocation));
        services.TryAddTransient<IDependencyFileManager, DependencyFileManager>();
        services.TryAddTransient<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, sp.GetRequiredService<IVmrInfo>().TmpPath.ToString()));
        services.TryAddTransient<ILocalGitRepoFactory, LocalGitRepoFactory>();
        services.TryAddTransient<ILocalGitClient, LocalGitClient>();
        services.TryAddTransient<ILocalLibGit2Client, LocalLibGit2Client>();
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddTransient<IVmrPatchHandler, VmrPatchHandler>();
        services.TryAddTransient<IVmrUpdater, VmrUpdater>();
        services.TryAddTransient<IVmrInitializer, VmrInitializer>();
        services.TryAddTransient<IVmrRemover, VmrRemover>();
        services.TryAddTransient<IVmrBackFlower, VmrBackFlower>();
        services.TryAddTransient<IVmrForwardFlower, VmrForwardFlower>();
        services.TryAddTransient<ICodeFlowVmrUpdater, CodeFlowVmrUpdater>();
        services.TryAddTransient<IBackflowConflictResolver, BackflowConflictResolver>();
        services.TryAddTransient<IForwardFlowConflictResolver, ForwardFlowConflictResolver>();
        services.TryAddTransient<IFlatJsonUpdater, FlatJsonUpdater>();
        services.TryAddTransient<IJsonFileMerger, JsonFileMerger>();
        services.TryAddTransient<IVersionDetailsFileMerger, VersionDetailsFileMerger>();
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
        services.TryAddScoped<ICommentCollector, CommentCollector>();

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
}
