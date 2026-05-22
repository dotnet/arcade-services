// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;
using DataModels = Maestro.Data.Models;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="SubscriptionTriggerOutcome"/>s.
/// </summary>
[Route("subscription-trigger-outcomes")]
[ApiVersion("2020-02-20")]
public class SubscriptionTriggerOutcomesController : ControllerBase
{
    private const int DefaultResultLimit = 100;
    private const int MaxResultLimit = 1000;

    private readonly BuildAssetRegistryContext _context;

    public SubscriptionTriggerOutcomesController(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets the N latest <see cref="SubscriptionTriggerOutcome"/>s that match the given filters, ordered by date descending.
    /// </summary>
    /// <param name="subscriptionId">Filter by subscription id.</param>
    /// <param name="buildId">Filter by build id.</param>
    /// <param name="date">Return only outcomes on or before this date. Include an explicit offset (e.g. "2025-01-15T12:00:00Z").</param>
    /// <param name="subscriptionOutcomeType">Filter by outcome type (e.g. "Updated", "NoUpdate", "Failure").</param>
    /// <param name="operationId">Filter by operation id.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SubscriptionTriggerOutcome>), Description = "The list of subscription outcomes")]
    [ValidateModelState]
    public async Task<IActionResult> ListSubscriptionOutcomes(
        string? subscriptionId = null,
        int? buildId = null,
        DateTimeOffset? date = null,
        string? subscriptionOutcomeType = null,
        string? operationId = null,
        [Range(1, MaxResultLimit)] int limit = DefaultResultLimit)
    {
        IQueryable<DataModels.SubscriptionOutcome> query = _context.SubscriptionOutcomes;

        Guid? subId = null;
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            try
            {
                subId = Guid.Parse(subscriptionId);
            }
            catch (FormatException)
            {
                return BadRequest(new
                {
                    message = "subscriptionId must be a valid GUID."
                });
            }
        }

        OutcomeType? parsedType = null;
        if (!string.IsNullOrEmpty(subscriptionOutcomeType))
        {
            if (!Enum.TryParse<OutcomeType>(subscriptionOutcomeType, ignoreCase: true, out var typeValue)
                || !Enum.IsDefined(typeof(OutcomeType), typeValue))
            {
                return BadRequest(new
                {
                    message = $"subscriptionOutcomeType must be one of: {string.Join(", ", Enum.GetNames(typeof(OutcomeType)))}."
                });
            }

            parsedType = typeValue;
        }

        if (subId.HasValue)
        {
            query = query.Where(o => o.SubscriptionId == subId.Value);
        }

        if (buildId.HasValue)
        {
            query = query.Where(o => o.BuildId == buildId.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(o => o.Date <= date.Value);
        }

        if (parsedType.HasValue)
        {
            var mappedType = (DataModels.SubscriptionOutcomeType)parsedType.Value;
            query = query.Where(o => o.Type == mappedType);
        }

        if (!string.IsNullOrEmpty(operationId))
        {
            query = query.Where(o => o.OperationId == operationId);
        }

        var results = await query
            .OrderByDescending(o => o.Date)
            .Take(limit)
            .ToListAsync();

        return Ok(results.Select(o => new SubscriptionTriggerOutcome(o)).ToList());
    }

    /// <summary>
    ///   Gets a single <see cref="SubscriptionTriggerOutcome"/> by its operation id.
    /// </summary>
    /// <param name="operationId">The operation id of the <see cref="SubscriptionTriggerOutcome"/>.</param>
    [HttpGet("{operationId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(SubscriptionTriggerOutcome), Description = "The requested SubscriptionTriggerOutcome")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "No subscription outcome was found for the supplied operation id")]
    [ValidateModelState]
    public async Task<IActionResult> GetSubscriptionOutcome([FromRoute][Required] string operationId)
    {
        var outcome = await _context.SubscriptionOutcomes
            .FirstOrDefaultAsync(o => o.OperationId == operationId);

        if (outcome == null)
        {
            return NotFound();
        }

        return Ok(new SubscriptionTriggerOutcome(outcome));
    }
}
