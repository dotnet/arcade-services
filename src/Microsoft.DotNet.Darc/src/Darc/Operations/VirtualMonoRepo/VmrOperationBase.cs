// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class VmrOperationBase : Operation
{
    private readonly VmrCommandLineOptions _options;

    protected VmrOperationBase(VmrCommandLineOptions options) : base(options, RegisterServices(options))
    {
        _options = options;
    }

    /// <summary>
    /// Executes any VMR command by running it for every repository from the arguments.
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        if (!repositories.Any())
        {
            Logger.LogError("Please specify at least one repository to synchronize");
            return Constants.ErrorCode;
        }

        var vmrManager = Provider.GetRequiredService<IVmrManager>();

        IEnumerable<(SourceMapping Mapping, string? Revision)> reposToSync;

        // 'all' means sync all of the repos
        if (repositories.Count() == 1 && repositories.First() == "all")
        {
            reposToSync = vmrManager.Mappings.Select<SourceMapping, (SourceMapping Mapping, string? Revision)>(m => (m, null));
        }
        else
        {
            var repoNamesWithRevisions = repositories
                .Select(a => a.Split(':') is string[] parts && parts.Length == 2
                    ? (Name: parts[0], Revision: parts[1])
                    : (a, null));

            reposToSync = repoNamesWithRevisions
                .Select<(string Name, string? Revision), (SourceMapping Mapping, string? Revision)>(repo => (
                    vmrManager.Mappings.FirstOrDefault(m => m.Name.Equals(repo.Name, StringComparison.InvariantCultureIgnoreCase)) ?? throw new Exception($"No repo named '{repo.Name}' found"),
                    repo.Revision));
        }

        var success = true;

        // We have a graceful cancellation to not leave the git repo in some inconsistent state
        // This is mainly useful for manual use but can be also useful in CI when we time out but still want to push what we committed
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        try
        {
            foreach (var (mapping, revision) in reposToSync)
            {
                listener.Token.ThrowIfCancellationRequested();
                success &= await ExecuteAsync(vmrManager, mapping, revision, listener.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return Constants.ErrorCode;
        }

        if (listener.Token.IsCancellationRequested)
        {
            return Constants.ErrorCode;
        }

        return success ? Constants.SuccessCode : Constants.ErrorCode;
    }

    protected abstract Task ExecuteInternalAsync(
        IVmrManager vmrManager,
        SourceMapping mapping,
        string? targetRevision,
        CancellationToken cancellationToken);

    private async Task<bool> ExecuteAsync(
        IVmrManager vmrManager,
        SourceMapping mapping,
        string? targetRevision,
        CancellationToken cancellationToken)
    {
        using (Logger.BeginScope(mapping.Name))
        {
            try
            {
                await ExecuteInternalAsync(vmrManager, mapping, targetRevision, cancellationToken);
                return true;
            }
            catch (EmptySyncException e)
            {
                Logger.LogInformation("{message}", e.Message);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to synchronize repo {name}{exception}", mapping.Name, Environment.NewLine + e.Message);
                Logger.LogDebug("{exception}", e);
                return false;
            }
        }
    }

    private static IServiceCollection RegisterServices(VmrCommandLineOptions options)
    {
        var services = new ServiceCollection();
        services.TryAddTransient<IProcessManager>(s => ActivatorUtilities.CreateInstance<ProcessManager>(s, options.GitLocation));
        services.TryAddSingleton<ISourceMappingParser, SourceMappingParser>();
        services.TryAddSingleton<IVmrManagerFactory, VmrManagerFactory>();
        services.TryAddSingleton<IRemoteFactory>(_ => new RemoteFactory(options));
        services.TryAddSingleton<IVmrManager>(s =>
        {
            var processManager = s.GetRequiredService<IProcessManager>();
            var logger = s.GetRequiredService<ILogger<DarcSettings>>();
            var factory = s.GetRequiredService<IVmrManagerFactory>();

            var vmrPath = options.VmrPath ?? processManager.FindGitRoot(Directory.GetCurrentDirectory());
            var tmpPath = options.TmpPath ?? LocalSettings.GetDarcSettings(options, logger).TemporaryRepositoryRoot;

            return factory.CreateVmrManager(s, vmrPath, tmpPath).GetAwaiter().GetResult();
        });

        return services;
    }
}
