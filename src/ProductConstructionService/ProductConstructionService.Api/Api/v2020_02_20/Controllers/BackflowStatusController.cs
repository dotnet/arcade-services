// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Models;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
/// Exposes methods to trigger and retrieve VMR backflow validation status.
/// </summary>
[Route("backflow-status")]
[ApiVersion("2020-02-20")]
public class BackflowStatusController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<BackflowStatusController> _logger;

    public BackflowStatusController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        IRedisCacheFactory redisCacheFactory,
        ILogger<BackflowStatusController> logger)
    {
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    /// <summary>
    /// Trigger backflow status calculation for a VMR build.
    /// </summary>
    /// <param name="vmrBuildId">VMR build ID which will be resolved to a commit SHA</param>
    [HttpPost("trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Backflow status calculation has been triggered")]
    [ValidateModelState]
    public async Task<IActionResult> TriggerBackflowStatusCalculation(
        [FromQuery(Name = "vmr-build-id")][Required] int vmrBuildId)
    {
        // Validate that the build exists
        var build = await _context.Builds
            .FirstOrDefaultAsync(b => b.Id == vmrBuildId);

        if (build == null)
        {
            return NotFound(new ApiError($"Build {vmrBuildId} was not found"));
        }

        // Enqueue work item to the codeflow queue
        var workItem = new BackflowValidationWorkItem
        {
            VmrBuildId = vmrBuildId
        };

        await _workItemProducerFactory
            .CreateProducer<BackflowValidationWorkItem>(IsCodeFlowSubscription: true)
            .ProduceWorkItemAsync(workItem);

        _logger.LogInformation(
            "Enqueued backflow status calculation for VMR build {buildId}",
            vmrBuildId);

        return Accepted();
    }

    /// <summary>
    /// Get cached backflow status for a VMR commit.
    /// </summary>
    /// <param name="vmrBuildId">VMR build ID to retrieve status for</param>
    [HttpGet("{vmrBuildId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BackflowStatus), Description = "The cached backflow status")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "No cached status found for this SHA")]
    [ValidateModelState]
    public async Task<IActionResult> GetBackflowStatus([FromRoute][Required] int vmrBuildId)
    {
        // Validate that the build exists
        var build = await _context.Builds
            .FirstOrDefaultAsync(b => b.Id == vmrBuildId);

        if (build == null)
        {
            return NotFound(new ApiError($"Build {vmrBuildId} was not found"));
        }

        var cache = _redisCacheFactory.Create<BackflowStatus>(build.Commit, includeTypeInKey: true);
        var status = await cache.TryGetStateAsync();

        if (status == null)
        {
            return NotFound(new ApiError($"No backflow status found for VMR SHA {build.Commit}"));
        }

        return Ok(status);
    }
}
