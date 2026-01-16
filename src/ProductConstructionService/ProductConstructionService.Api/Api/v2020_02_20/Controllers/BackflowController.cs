// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Models;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
/// Exposes methods to trigger and retrieve VMR backflow validation status.
/// </summary>
[Route("backflow")]
[ApiVersion("2020-02-20")]
public class BackflowController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<BackflowController> _logger;

    public BackflowController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        IRedisCacheFactory redisCacheFactory,
        ILogger<BackflowController> logger)
    {
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    /// <summary>
    /// Trigger backflow status calculation for a VMR commit.
    /// </summary>
    /// <param name="vmrCommitSha">VMR commit SHA to calculate status for</param>
    /// <param name="vmrBuildId">Optional VMR build ID which resolves to a SHA</param>
    [HttpPost("trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Backflow status calculation has been triggered")]
    [ValidateModelState]
    public async Task<IActionResult> TriggerBackflowStatusCalculation(
        [FromQuery][Required] string vmrCommitSha,
        [FromQuery(Name = "vmr-build-id")] int? vmrBuildId = null)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(vmrCommitSha))
        {
            return BadRequest(new ApiError("vmrCommitSha is required"));
        }

        // If build ID is provided, validate it exists
        if (vmrBuildId.HasValue)
        {
            var build = await _context.Builds
                .FirstOrDefaultAsync(b => b.Id == vmrBuildId.Value);

            if (build == null)
            {
                return NotFound(new ApiError($"Build {vmrBuildId.Value} was not found"));
            }
        }

        // Enqueue work item to the codeflow queue
        var workItem = new BackflowValidationWorkItem
        {
            VmrCommitSha = vmrCommitSha,
            VmrBuildId = vmrBuildId
        };

        await _workItemProducerFactory
            .CreateProducer<BackflowValidationWorkItem>(isCodeFlowSubscription: true)
            .ProduceWorkItemAsync(workItem);

        _logger.LogInformation(
            "Enqueued backflow status calculation for VMR SHA {sha} (build ID: {buildId})",
            vmrCommitSha,
            vmrBuildId);

        return Accepted();
    }

    /// <summary>
    /// Get cached backflow status for a VMR commit.
    /// </summary>
    /// <param name="vmrCommitSha">VMR commit SHA to retrieve status for</param>
    [HttpGet("{vmrCommitSha}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BackflowStatus), Description = "The cached backflow status")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "No cached status found for this SHA")]
    [ValidateModelState]
    public async Task<IActionResult> GetBackflowStatus([FromRoute][Required] string vmrCommitSha)
    {
        if (string.IsNullOrWhiteSpace(vmrCommitSha))
        {
            return BadRequest(new ApiError("vmrCommitSha is required"));
        }

        var cache = _redisCacheFactory.Create<BackflowStatus>(vmrCommitSha, includeTypeInKey: true);
        var status = await cache.TryGetStateAsync();

        if (status == null)
        {
            return NotFound(new ApiError($"No backflow status found for VMR SHA {vmrCommitSha}"));
        }

        return Ok(status);
    }
}
