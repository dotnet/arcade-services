// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using System.Linq;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class VmrOperationBase : Operation
{
    private bool _runCanceled = false;
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

        var vmrManagerFactory = Provider.GetRequiredService<IVmrManagerFactory>();
        var vmrManager = await vmrManagerFactory.CreateVmrManager(_options.VmrPath, _options.TmpPath);

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
        CancellationToken cancellationToken = ListenForEscapeKey(Logger);

        try
        {
            foreach (var (mapping, revision) in reposToSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                success &= await ExecuteAsync(vmrManager, Logger, mapping, revision, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_runCanceled)
            {
                Environment.Exit(Constants.ErrorCode);
            }

            return Constants.ErrorCode;
        }

        if (_runCanceled)
        {
            Environment.Exit(Constants.ErrorCode);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Constants.ErrorCode;
        }

        return success ? Constants.SuccessCode : Constants.ErrorCode;
    }

    protected abstract Task ExecuteInternalAsync(
        IVmrManager vmrManager,
        ILogger logger,
        SourceMapping mapping,
        string? targetRevision,
        CancellationToken cancellationToken);

    private async Task<bool> ExecuteAsync(
        IVmrManager vmrManager,
        ILogger logger,
        SourceMapping mapping,
        string? targetRevision,
        CancellationToken cancellationToken)
    {
        using (logger.BeginScope(mapping.Name))
        {
            try
            {
                await ExecuteInternalAsync(vmrManager, logger, mapping, targetRevision, cancellationToken);
                return true;
            }
            catch (EmptySyncException e)
            {
                logger.LogInformation("{message}", e.Message);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError("Failed to synchronize repo {name}{exception}", mapping.Name, Environment.NewLine + e.Message);
                logger.LogDebug("{exception}", e);
                return false;
            }
        }
    }

    /// <summary>
    /// Listens for user's key presses and triggers a cancellation when ESC / Space is pressed.
    /// This is used for graceful cancellation to not leave the git repo in some inconsistent state.
    /// This is mainly useful for manual use but can be also useful in CI when we time out but still want to push what we committed.
    /// </summary>
    protected CancellationToken ListenForEscapeKey(ILogger logger)
    {
        // Key read might not be available in all scenarios
        if (Console.IsInputRedirected)
        {
            return CancellationToken.None;
        }

        var cancellationSource = new CancellationTokenSource();

        void CancelRun()
        {
            logger.LogWarning("Run interrupted by user, stopping synchronization...");
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += new ConsoleCancelEventHandler((object? sender, ConsoleCancelEventArgs args) =>
        {
            args.Cancel = true;
            _runCanceled = true;
            CancelRun();
        });

        new Thread(() =>
        {
            ConsoleKeyInfo keyInfo;

            do
            {
                while (!Console.KeyAvailable)
                {
                    Thread.Sleep(250);
                }

                keyInfo = Console.ReadKey(true);
            } while (keyInfo.Key != ConsoleKey.Escape && keyInfo.Key != ConsoleKey.Spacebar);

            CancelRun();
        }).Start();

        return cancellationSource.Token;
    }

    private static IServiceCollection RegisterServices(VmrCommandLineOptions options) =>
        new ServiceCollection()
            .AddVmrManager(options.GitLocation);
}
