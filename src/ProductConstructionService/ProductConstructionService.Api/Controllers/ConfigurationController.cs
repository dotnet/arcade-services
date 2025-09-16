// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using ProductConstructionService.Api.Configuration;
using YamlDotNet.Serialization;

namespace ProductConstructionService.Api.Controllers;

[Route("configuration")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class ConfigurationController(
    IRemoteFactory remoteFactory) : Controller
{
    private readonly IRemoteFactory _remoteFactory = remoteFactory;

    private const string SubscriptionsConfigPath = "subscriptions";

    [HttpGet(Name = "refresh")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Makes PCS refresh in memory subscription configuration")]
    public async Task<IActionResult> RefreshConfiguration(string repoUri, string branch)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(repoUri);

        var branchSha = await remote.GetLatestCommitAsync(repoUri, branch);
        var files = await remote.GetFilesAtCommitAsync(repoUri, branchSha, SubscriptionsConfigPath);

        IDeserializer serializer = new DeserializerBuilder().Build();
        var subscriptionsData = files.SelectMany(f => serializer.Deserialize<List<SubscriptionUpdateYamlData>>(f.Content)).ToList();

        return Ok();
    }
}
