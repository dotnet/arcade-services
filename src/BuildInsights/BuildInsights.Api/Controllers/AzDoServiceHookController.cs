// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Configuration.Models;
using BuildInsights.Api.Controllers.Models;
using BuildInsights.KnownIssues.WorkItems;
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
    public async Task<IActionResult> BuildCompleted([FromBody] CompletedBuildMessage message)
    {
        if (!ValidateMessage(message, CompletedBuildMessage.MessageEventType))
        {
            return BadRequest();
        }

        await RequestBuildAnalysis(new BuildAnalysisRequestWorkItem
        {
            OrganizationId = message.Resource.OrgId,
            ProjectId = message.ResourceContainers.Project.Id,
            BuildId = message.Resource.Id,
        });

        return Ok();
    }

    [HttpPost(KnownIssueReprocessingMessage.MessageEventType)]
    public async Task<IActionResult> KnownIssueReprocessing([FromBody] KnownIssueReprocessingMessage message)
    {
        if (!ValidateMessage(message, KnownIssueReprocessingMessage.MessageEventType))
        {
            return BadRequest();
        }

        await RequestBuildAnalysis(new BuildAnalysisRequestWorkItem
        {
            OrganizationId = message.OrganizationId,
            ProjectId = message.ProjectId,
            BuildId = message.BuildId,
        });

        return Ok();
    }

    [HttpPost(PipelineStateChangedMessage.RunStateChangedEventType)]
    [HttpPost(PipelineStateChangedMessage.StageStateChangedEventType)]
    public async Task<IActionResult> PipelineStateChanged([FromBody] PipelineStateChangedMessage message)
    {
        if (!ValidateSecretHeader())
        {
            return BadRequest();
        }

        BuildAnalysisRequestWorkItem request;

        try
        {
            request = new BuildAnalysisRequestWorkItem
            {
                OrganizationId = message.GetOrgId(),
                ProjectId = message.GetProjectId(),
                BuildId = message.Resource.Id,
            };
        }
        catch (InvalidOperationException)
        {
            return BadRequest();
        }

        await RequestBuildAnalysis(request);

        return Ok();
    }

    private async Task RequestBuildAnalysis(BuildAnalysisRequestWorkItem request)
    {
        var producer = _workItemProducerFactory.CreateProducer<BuildAnalysisRequestWorkItem>();
        await producer.ProduceWorkItemAsync(request);
    }

    private bool ValidateSecretHeader()
    {
        var headerValue = Request.Headers[_serviceHookSettings.SecretHttpHeaderName].FirstOrDefault();
        return string.Equals(headerValue, _serviceHookSettings.SecretHttpHeaderValue, StringComparison.OrdinalIgnoreCase);
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
