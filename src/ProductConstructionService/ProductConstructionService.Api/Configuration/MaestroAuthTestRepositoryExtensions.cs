// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;

namespace ProductConstructionService.Api.Configuration;

public static class MaestroAuthTestRepositoryExtensions
{
    /// <summary>
    /// Makes sure we redirect any requests for GitHub dotnet repositories to maestro-auth-test.
    /// The GitHub application used in local runs does not have access to the real repositories.
    /// The forks under the maestro-auth-test organization are used instead.
    /// Used when running the service locally.
    /// </summary>
    public static void UseMaestroAuthTestRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRepositoryCloneManager, MaestroAuthTestRepositoryCloneManager>();
        services.AddScoped<RepositoryCloneManager>();
    }

    private class MaestroAuthTestRepositoryCloneManager : IRepositoryCloneManager
    {
        private readonly RepositoryCloneManager _cloneManager;

        public MaestroAuthTestRepositoryCloneManager(RepositoryCloneManager cloneManager)
        {
            _cloneManager = cloneManager;
        }

        public async Task<ILocalGitRepo> PrepareCloneAsync(
            SourceMapping mapping,
            IReadOnlyCollection<string> remoteUris,
            IReadOnlyCollection<string> requestedRefs,
            string checkoutRef,
            bool resetToRemote = false,
            CancellationToken cancellationToken = default)
        {
            return await _cloneManager.PrepareCloneAsync(mapping, ReplaceDotnetWithMaestroAuthTest(remoteUris), requestedRefs, checkoutRef, resetToRemote, cancellationToken);
        }

        public async Task<ILocalGitRepo> PrepareCloneAsync(
            string repoUri,
            string checkoutRef,
            bool resetToRemote = false,
            CancellationToken cancellationToken = default)
        {
            return await _cloneManager.PrepareCloneAsync(ReplaceDotnetWithMaestroAuthTest(repoUri), checkoutRef, resetToRemote, cancellationToken);
        }

        public async Task<ILocalGitRepo> PrepareCloneAsync(
            SourceMapping mapping,
            IReadOnlyCollection<string> remoteUris,
            string checkoutRef,
            bool resetToRemote = false,
            CancellationToken cancellationToken = default)
        {
            return await _cloneManager.PrepareCloneAsync(mapping, ReplaceDotnetWithMaestroAuthTest(remoteUris), checkoutRef, resetToRemote, cancellationToken);
        }

        public Task<ILocalGitRepo> PrepareCloneAsync(NativePath clonePath, IReadOnlyCollection<string> remoteUris, IReadOnlyCollection<string> requestedRefs, string checkoutRef, bool resetToRemote = false, CancellationToken cancellationToken = default)
        {
            return _cloneManager.PrepareCloneAsync(clonePath, ReplaceDotnetWithMaestroAuthTest(remoteUris), requestedRefs, checkoutRef, resetToRemote, cancellationToken);
        }

        private static IReadOnlyCollection<string> ReplaceDotnetWithMaestroAuthTest(IReadOnlyCollection<string> uris)
            => [.. uris.Select(ReplaceDotnetWithMaestroAuthTest)];

        private static string ReplaceDotnetWithMaestroAuthTest(string uri)
            => uri.Replace("github.com/dotnet/", "github.com/maestro-auth-test/");
    }
}
