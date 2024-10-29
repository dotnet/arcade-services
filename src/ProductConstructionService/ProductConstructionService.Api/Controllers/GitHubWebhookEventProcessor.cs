// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Octokit.Internal;
using Octokit;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;
using Octokit.Webhooks.Events.Installation;
using Octokit.Webhooks.Events.InstallationRepositories;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace ProductConstructionService.Api.Controllers;

public class GitHubWebhookEventProcessor : WebhookEventProcessor
{
    private readonly ILogger<GitHubWebhookEventProcessor> _logger;
    private readonly IGitHubTokenProvider _tokenProvider;
    private readonly BuildAssetRegistryContext _context;

    public GitHubWebhookEventProcessor(
        ILogger<GitHubWebhookEventProcessor> logger,
        IGitHubTokenProvider tokenProvider,
        BuildAssetRegistryContext context)
    {
        _logger = logger;
        _tokenProvider = tokenProvider;
        _context = context;
    }

    protected override async Task ProcessInstallationWebhookAsync(
        WebhookHeaders headers,
        InstallationEvent installationEvent,
        InstallationAction action)
    {
        switch(installationEvent.Action)
        {
            case InstallationActionValue.Deleted:
                await RemoveInstallationRepositoriesAsync(installationEvent.Installation.Id);
                break;
            case InstallationActionValue.Created:
                await SynchronizeInstallationRepositoriesAsync(installationEvent.Installation.Id);
                break;
            default:
                _logger.LogError("Received unknown installation action `{action}`", installationEvent.Action);
                break;
        }
    }

    protected override async Task ProcessInstallationRepositoriesWebhookAsync(WebhookHeaders headers, InstallationRepositoriesEvent installationRepositoriesEvent, InstallationRepositoriesAction action)
    {
        await SynchronizeInstallationRepositoriesAsync(installationRepositoriesEvent.Installation.Id);
    }

    private async Task RemoveInstallationRepositoriesAsync(long installationId)
    {
        foreach (Maestro.Data.Models.Repository repo in await _context.Repositories.Where(r => r.InstallationId == installationId).ToListAsync())
        {
            repo.InstallationId = 0;
            _context.Update(repo);
        }

        await _context.SaveChangesAsync();
    }

    private async Task SynchronizeInstallationRepositoriesAsync(long installationId)
    {
        var token = await _tokenProvider.GetTokenForInstallationAsync(installationId);
        IReadOnlyList<Repository> gitHubRepos = await GetAllInstallationRepositories(token);

        List<Maestro.Data.Models.Repository> barRepos =
            await _context.Repositories.Where(r => r.InstallationId == installationId).ToListAsync();

        List<Maestro.Data.Models.Repository> updatedRepos = [];
        // Go through all Repos that currently have a given installation id. If the repo is already present in BAR, make sure it has the correct installationId,
        // and add it to the list of update repos.
        // If not, add it to BAR
        foreach (Repository repo in gitHubRepos)
        {
            Maestro.Data.Models.Repository? existing = await _context.Repositories.FindAsync(repo.HtmlUrl);

            if (existing == null)
            {
                _context.Repositories.Add(
                    new Maestro.Data.Models.Repository
                    {
                        RepositoryName = repo.HtmlUrl,
                        InstallationId = installationId,
                    });
            }
            else
            {
                existing.InstallationId = installationId;
                _context.Update(existing);
                updatedRepos.Add(existing);
            }
        }

        // If a repo is present in BAR, but not in the updateRepos list, that means the GH app was uninstalled from it. Set its installationID to 0
        foreach (Maestro.Data.Models.Repository repo in barRepos.Except(updatedRepos, new RepositoryNameComparer()))
        {
            repo.InstallationId = 0;
            _context.Update(repo);
        }

        await _context.SaveChangesAsync();
    }

    private static Task<IReadOnlyList<Repository>> GetAllInstallationRepositories(string token)
    {
        var product = new ProductHeaderValue(
            "Maestro",
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion);
        var client = new GitHubClient(product) { Credentials = new Credentials(token, AuthenticationType.Bearer) };
        var pagination = new ApiPagination();
        var uri = new Uri("installation/repositories", UriKind.Relative);

        async Task<IApiResponse<List<Repository>>> GetInstallationRepositories(Uri requestUri)
        {
            IApiResponse<InstallationRepositoriesResponse> response =
                await client.Connection.Get<InstallationRepositoriesResponse>(requestUri, parameters: null);
            return new ApiResponse<List<Repository>>(response.HttpResponse, response.Body.Repositories);
        }

        return pagination.GetAllPages<Repository>(
            async () => new ReadOnlyPagedCollection<Repository>(
                await GetInstallationRepositories(uri),
                GetInstallationRepositories),
            uri);
    }

    public class InstallationRepositoriesResponse
    {
        public int TotalCount { get; set; }
        public StringEnum<InstallationRepositorySelection> RepositorySelection { get; set; }
        public required List<Repository> Repositories { get; set; }
    }

    private class RepositoryNameComparer : IEqualityComparer<Maestro.Data.Models.Repository>
    {
        public bool Equals(Maestro.Data.Models.Repository? x, Maestro.Data.Models.Repository? y) =>
            string.Equals(x?.RepositoryName, y?.RepositoryName);

        public int GetHashCode([DisallowNull] Maestro.Data.Models.Repository obj) => throw new NotImplementedException();
    }
}
