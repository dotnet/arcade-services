// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("configuration")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class ConfigurationController(
        IConfigurationDataIngestor configurationDataIngestor,
        IGitHubInstallationIdResolver gitHubInstallationIdResolver,
        BuildAssetRegistryContext context,
        ILogger<ConfigurationController> logger)
    : Controller
{
    [HttpGet(Name = "refresh")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ConfigurationIngestResults), Description = "Refresh subscription configuration from a given source and return ingestion stats")]
    public async Task<IActionResult> RefreshConfiguration(string repoUri, string branch)
    {
        try
        {
            ConfigurationIngestResults stats = await configurationDataIngestor.IngestConfiguration(repoUri, branch);

            logger.LogInformation("Configuration refresh stats: {stats}", stats);

            if (stats.Subscriptions.Added > 0 || stats.Subscriptions.Updated > 0)
            {
                IActionResult? error = await EnsureRepositoryInstallationIds(repoUri, branch);
                return error ?? Ok(stats);
            }

            return Ok(stats);
        }
        catch (Exception e)
        {
            return BadRequest(new ApiError(e.Message));
        }
    }

    private async Task<IActionResult?> EnsureRepositoryInstallationIds(string repoUri, string branch)
    {
        logger.LogInformation("Verifying installation IDs for any new repositories...");

        List<Repository> allRepos = await context.Repositories.ToListAsync();
        ConfigurationSource configurationSource = await context.ConfigurationSources
            .Include(cs => cs.Subscriptions)
            .FirstAsync(cs => cs.Uri == repoUri && cs.Branch == branch);

        foreach (Subscription subscription in configurationSource.Subscriptions.Where(s => s.TargetRepository.Contains("github.com/")))
        {
            var existingRepo = allRepos.FirstOrDefault(r => r.RepositoryName == subscription.TargetRepository);
            if (existingRepo == null || existingRepo.InstallationId == 0)
            {
                long? installationId = await gitHubInstallationIdResolver.GetInstallationIdForRepository(subscription.TargetRepository);

                if (!installationId.HasValue)
                {
                    return BadRequest(new ApiError($"Could not determine installation ID for repository {subscription.TargetRepository}. Ensure the repository exists and the GitHub App is installed."));
                }

                if (existingRepo == null)
                {
                    await context.Repositories.AddAsync(
                        new Repository
                        {
                            RepositoryName = subscription.TargetRepository,
                            InstallationId = installationId.Value,
                        });
                }
                else
                {
                    existingRepo.InstallationId = installationId.Value;
                }

                await context.SaveChangesAsync();
            }
        }

        return null;
    }

    [HttpDelete(Name = "delete")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ConfigurationIngestResults), Description = "Delete subscription configuration from a given source and return stats of removed entities")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Type = typeof(ApiError), Description = "Cannot delete configuration for the specified branch")]
    public async Task<IActionResult> ClearConfiguration(string repoUri, string branch)
    {
        if (string.IsNullOrEmpty(repoUri) || branch == "staging" || branch == "production")
        {
            return BadRequest(new ApiError("Cannot delete configuration for the specified branch"));
        }

        try
        {
            ConfigurationIngestResults stats = await configurationDataIngestor.ClearConfiguration(repoUri, branch);
            return Ok(stats);
        }
        catch (Exception e)
        {
            return BadRequest(new ApiError(e.Message));
        }
    }
}
