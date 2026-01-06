// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.DataProviders;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("configuration-ingestion")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class ConfigurationIngestionController : Controller
{
    private readonly IConfigurationIngestor _configurationIngestor;
    private readonly ISqlBarClient _sqlBarClient;
    private readonly ILogger<ConfigurationIngestionController> _logger;

    private const string ProductionNamespaceName = "production";

    public ConfigurationIngestionController(IConfigurationIngestor configurationIngestor, ISqlBarClient sqlBarClient, ILogger<ConfigurationIngestionController> logger)
    {
        _configurationIngestor = configurationIngestor;
        _sqlBarClient = sqlBarClient;
        _logger = logger;
    }

    [HttpPost(Name = "ingest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ConfigurationUpdates), Description = "Makes PCS ingest a namespace configuration")]
    public async Task<IActionResult> IngestNamespace(
        [FromQuery] string namespaceName,
        [FromBody] YamlConfiguration yamlConfiguration,
        [FromBody] bool saveChanges = true)
    {
        if (namespaceName == ProductionNamespaceName)
        {
            saveChanges = false;
        }

        _logger.LogInformation("Ingesting configuration for namespace {NamespaceName} (saveChanges={SaveChanges})", namespaceName, saveChanges);
        try
        {
            var updates = await _configurationIngestor.IngestConfigurationAsync(
                new ConfigurationData(
                    yamlConfiguration.Subscriptions,
                    yamlConfiguration.Channels,
                    yamlConfiguration.DefaultChannels,
                    yamlConfiguration.BranchMergePolicies),
                namespaceName,
                saveChanges);

            return Ok(updates);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Configuration validation failed",
                Detail = ex.Message,
                Status = 400
            });
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "BAR constrains violated",
                Detail = ex.Message,
                Status = 400
            });
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
