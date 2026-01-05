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
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("ingestion")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class IngestionController : Controller
{
    private readonly IConfigurationIngestor _configurationIngestor;
    private readonly ISqlBarClient _sqlBarClient;

    private const string ProductionNamespaceName = "production";

    public IngestionController(IConfigurationIngestor configurationIngestor, ISqlBarClient sqlBarClient)
    {
        _configurationIngestor = configurationIngestor;
        _sqlBarClient = sqlBarClient;
    }

    [HttpPost(Name = "ingest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Makes PCS ingest a namespace configuration")]
    public async Task<IActionResult> IngestNamespace(
        string namespaceName,
        [FromBody] YamlConfiguration yamlConfiguration,
        bool saveChanges = true)
    {
        if (namespaceName == ProductionNamespaceName)
        {
            saveChanges = false;
        }

        var updates = await _configurationIngestor.IngestConfigurationAsync(
            new ConfigurationData(
                yamlConfiguration.Subscriptions,
                yamlConfiguration.Channels,
                yamlConfiguration.DefaultChannels,
                yamlConfiguration.BranchMergePolicies),
            namespaceName,
            saveChanges);

        return Ok(true);
    }

    [HttpDelete(Name = "delete")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Makes PCS delete a namespace and all configuration inside of it")]
    public async Task<IActionResult> DeleteNamespace(string namespaceName, bool saveChanges)
    {
        if (namespaceName == ProductionNamespaceName)
        {
            saveChanges = false;
        }
        await _sqlBarClient.DeleteNamespaceAsync(namespaceName, saveChanges);

        return Ok(true);
    }
}
