// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public static class VmrRegistrations
{
    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        string vmrPath,
        string tmpPath,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        // Configuration based registrations
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        services.TryAddSingleton(new RemoteConfiguration(gitHubToken, azureDevOpsToken));
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, gitLocation));

        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<VmrManagerBase>>());
        services.TryAddTransient<IDependencyFileManager, DependencyFileManager>();
        services.TryAddTransient<IGitRepoFactory, VmrGitClientFactory>();
        services.TryAddTransient<ILocalGitClient, LocalGitClient>();
        services.TryAddTransient<ILocalLibGit2Client, LocalLibGit2Client>();
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddTransient<IVmrPatchHandler, VmrPatchHandler>();
        services.TryAddTransient<IVmrUpdater, VmrUpdater>();
        services.TryAddTransient<IVmrInitializer, VmrInitializer>();
        services.TryAddTransient<IVmrBackflower, CodeBackflower>();
        services.TryAddTransient<IVmrRepoVersionResolver, VmrRepoVersionResolver>();
        services.TryAddSingleton<IVmrDependencyTracker, VmrDependencyTracker>();
        services.TryAddTransient<IWorkBranchFactory, WorkBranchFactory>();
        services.TryAddTransient<IThirdPartyNoticesGenerator, ThirdPartyNoticesGenerator>();
        services.TryAddTransient<IReadmeComponentListGenerator, ReadmeComponentListGenerator>();
        services.TryAddTransient<ICodeownersGenerator, CodeownersGenerator>();
        services.TryAddTransient<IRepositoryCloneManager, RepositoryCloneManager>();
        services.TryAddTransient<IFileSystem, FileSystem>();
        services.TryAddTransient<IGitRepoCloner, GitNativeRepoCloner>();
        services.TryAddTransient<VmrCloakedFileScanner>();
        services.TryAddTransient<VmrBinaryFileScanner>();
        services.AddHttpClient("GraphQL", httpClient =>
        {
            httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, "application/json");
            httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, "Darc");
        }).ConfigureHttpMessageHandlerBuilder(handler =>
        {
            if (handler.PrimaryHandler is HttpClientHandler httpClientHandler)
            {
                httpClientHandler.CheckCertificateRevocationList = true;
            }
            else if (handler.PrimaryHandler is SocketsHttpHandler socketsHttpHandler)
            {
                socketsHttpHandler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.Online;
            }
            else
            {
                throw new InvalidOperationException($"Could not create client with CRL check, HttpMessageHandler type {handler.PrimaryHandler.GetType().FullName ?? handler.PrimaryHandler.GetType().Name} is unknown.");
            }
        });

        services.TryAddTransient<IVmrPusher, VmrPusher>();

        // These initialize the configuration by reading the JSON files in VMR's src/
        services.TryAddSingleton<ISourceManifest>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrInfo>();
            return SourceManifest.FromJson(configuration.GetSourceManifestPath());
        });

        return services;
    }
}
