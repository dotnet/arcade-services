// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.DataProviders;
using Maestro.DataProviders.ConfigurationIngestion;
using Maestro.DataProviders.Exceptions;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Controllers;

[Route("configuration-ingestion")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class ConfigurationIngestionController : Controller
{
    private readonly IConfigurationIngestor _configurationIngestor;
    private readonly ISqlBarClient _sqlBarClient;
    private readonly ILogger<ConfigurationIngestionController> _logger;
    private readonly IDistributedLock _distributedLock;

    private const string ProductionNamespaceName = "production";

    public ConfigurationIngestionController(
        IConfigurationIngestor configurationIngestor, 
        ISqlBarClient sqlBarClient, 
        ILogger<ConfigurationIngestionController> logger,
        IDistributedLock distributedLock)
    {
        _configurationIngestor = configurationIngestor;
        _sqlBarClient = sqlBarClient;
        _logger = logger;
        _distributedLock = distributedLock;
    }

    [HttpPost(Name = "ingest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ConfigurationUpdates), Description = "Makes PCS ingest a namespace configuration")]
    public async Task<IActionResult> IngestNamespace(
        [FromQuery] string namespaceName,
        [FromBody] YamlConfiguration yamlConfiguration,
        [FromQuery] bool saveChanges = true)
    {
        if (namespaceName == ProductionNamespaceName)
        {
            saveChanges = false;
        }

        _logger.LogInformation("Ingesting configuration for namespace {NamespaceName} (saveChanges={SaveChanges})", namespaceName, saveChanges);
        try
        {
            var updates = await _distributedLock.ExecuteWithLockAsync("ConfigurationIngestion", async () =>
            {
                return await _configurationIngestor.IngestConfigurationAsync(
                    new ConfigurationData(
                        yamlConfiguration.Subscriptions,
                        yamlConfiguration.Channels,
                        yamlConfiguration.DefaultChannels,
                        yamlConfiguration.BranchMergePolicies),
                    namespaceName,
                    saveChanges);
            });

            return Ok(updates);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Configuration validation failed for namespace {NamespaceName}", namespaceName);
            return BadRequest(new ApiError("Configuration validation failed", [ex.Message]));
        }
        catch (EntityIngestionValidationException ex)
        {
            _logger.LogError(ex, "Entity validation failed for namespace {NamespaceName}: {EntityInfo}", namespaceName, ex.EntityInfo);
            return BadRequest(new ApiError("Entity validation failed", [ex.Message]));
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException)
        {
            _logger.LogError(ex, "BAR constraints violated while ingesting namespace {NamespaceName}", namespaceName);
            return BadRequest(new ApiError("BAR constraints violated", [ex.Message]));
        }
    }

    [HttpDelete(Name = "delete")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Makes PCS delete a namespace and all configuration inside of it")]
    public async Task<IActionResult> DeleteNamespace(string namespaceName, bool saveChanges)
    {
        if (namespaceName == ProductionNamespaceName)
        {
            saveChanges = false;
        }
        _logger.LogInformation("Deleting namespace {NamespaceName} (saveChanges={SaveChanges})", namespaceName, saveChanges);
        await _sqlBarClient.DeleteNamespaceAsync(namespaceName, saveChanges);

        return Ok(true);
    }
}
