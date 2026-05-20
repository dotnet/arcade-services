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
///   Exposes methods to Read <see cref="SubscriptionOutcome"/>s.
/// </summary>
[Route("subscription-outcomes")]
[ApiVersion("2020-02-20")]
public class SubscriptionOutcomesController : ControllerBase
{
    private const int DefaultResultLimit = 100;
    private const int MaxResultLimit = 1000;

    private readonly BuildAssetRegistryContext _context;

    public SubscriptionOutcomesController(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets the N latest <see cref="SubscriptionOutcome"/>s that match the given filters, ordered by date descending.
    /// </summary>
    /// <param name="subscriptionId">Filter by subscription id.</param>
    /// <param name="buildId">Filter by build id.</param>
    /// <param name="date">Return only outcomes on or before this date (UTC).</param>
    /// <param name="type">Filter by outcome type.</param>
    /// <param name="operationId">Filter by operation id.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SubscriptionOutcome>), Description = "The list of subscription outcomes")]
    [ValidateModelState]
    public async Task<IActionResult> ListSubscriptionOutcomes(
        Guid? subscriptionId = null,
        int? buildId = null,
        DateTime? date = null,
        SubscriptionOutcomeType? type = null,
        string? operationId = null,
        [Range(1, MaxResultLimit)] int limit = DefaultResultLimit)
    {
        IQueryable<DataModels.SubscriptionOutcome> query = _context.SubscriptionOutcomes;

        if (subscriptionId.HasValue)
        {
            query = query.Where(o => o.SubscriptionId == subscriptionId.Value);
        }

        if (buildId.HasValue)
        {
            query = query.Where(o => o.BuildId == buildId.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(o => o.Date <= date.Value);
        }

        if (type.HasValue)
        {
            var mappedType = (DataModels.OutcomeType)type.Value;
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

        return Ok(results.Select(o => new SubscriptionOutcome(o)).ToList());
    }

    /// <summary>
    ///   Gets a single <see cref="SubscriptionOutcome"/> by its operation id.
    /// </summary>
    /// <param name="operationId">The operation id of the <see cref="SubscriptionOutcome"/>.</param>
    [HttpGet("{operationId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(SubscriptionOutcome), Description = "The requested SubscriptionOutcome")]
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

        return Ok(new SubscriptionOutcome(outcome));
    }
}
