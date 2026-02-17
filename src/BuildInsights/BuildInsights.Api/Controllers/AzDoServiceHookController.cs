// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Configuration.Models;
using BuildInsights.KnownIssues.WorkItems;
using BuildInsights.Utilities.AzureDevOps.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProductConstructionService.WorkItems;

namespace BuildInsights.Api.Controllers;

[Route("azdo/servicehooks")]
public class AzDoServiceHookController : ControllerBase
{
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly ServiceHookSettings _serviceHookSettings;

    public AzDoServiceHookController(
        IWorkItemProducerFactory workItemProducerFactory,
        IOptions<ServiceHookSettings> serviceHookSettings)
    {
        _serviceHookSettings = serviceHookSettings.Value;
        _workItemProducerFactory = workItemProducerFactory;
    }

    [HttpPost(CompletedBuildMessage.MessageEventType)]
    public async Task<IActionResult> BuildComplete([FromBody] CompletedBuildMessage message)
    {
        if (!ValidateMessage(message, CompletedBuildMessage.MessageEventType))
        {
            return BadRequest();
        }

        var producer = _workItemProducerFactory.CreateProducer<BuildAnalysisRequestWorkItem>();
        await producer.ProduceWorkItemAsync(new BuildAnalysisRequestWorkItem
        {
            OrganizationId = message.Resource.OrgId,
            ProjectId = message.ResourceContainers.Project.Id,
            BuildId = message.Resource.Id,
        });

        return Ok();
    }

    private bool ValidateMessage(AzureDevOpsEventBase message, string expectedEventType)
    {
        if (message == null)
        {
            return false;
        }

        if (!string.Equals(message.EventType, expectedEventType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var headerValue = Request.Headers[_serviceHookSettings.SecretHttpHeaderName].FirstOrDefault();
        if (!string.Equals(headerValue, _serviceHookSettings.SecretHttpHeaderValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
