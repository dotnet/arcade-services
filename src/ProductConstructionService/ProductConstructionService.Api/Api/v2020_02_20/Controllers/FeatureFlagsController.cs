// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
/// Exposes methods to manage feature flags for subscriptions.
/// </summary>
[Route("feature-flags")]
[ApiVersion("2020-02-20")]
public class FeatureFlagsController : ControllerBase
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagsController> _logger;

    public FeatureFlagsController(
        IFeatureFlagService featureFlagService,
        ILogger<FeatureFlagsController> logger)
    {
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Sets a feature flag for a specific subscription.
    /// </summary>
    /// <param name="request">The feature flag request.</param>
    /// <returns>The result of the operation.</returns>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(FeatureFlagResponse), Description = "Feature flag set successfully")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Type = typeof(FeatureFlagResponse), Description = "Invalid request or unknown flag")]
    [ValidateModelState]
    public async Task<IActionResult> SetFeatureFlag([FromBody] SetFeatureFlagRequest request)
    {
        if (request == null)
        {
            return BadRequest(new FeatureFlagResponse(false, "Request cannot be null"));
        }

        try
        {
            var expiry = request.ExpiryDays.HasValue ? (TimeSpan?)TimeSpan.FromDays(request.ExpiryDays.Value) : null;
            var result = await _featureFlagService.SetFlagAsync(
                request.SubscriptionId,
                request.FlagName,
                request.Value,
                expiry);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feature flag {FlagName} for subscription {SubscriptionId}",
                request.FlagName, request.SubscriptionId);
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }

    /// <summary>
    /// Gets all feature flags for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <returns>All feature flags for the subscription.</returns>
    [HttpGet("{subscriptionId:guid}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(FeatureFlagListResponse), Description = "The feature flags for the subscription")]
    [ValidateModelState]
    public async Task<IActionResult> GetFeatureFlags([FromRoute] Guid subscriptionId)
    {
        try
        {
            var flags = await _featureFlagService.GetFlagsForSubscriptionAsync(subscriptionId);
            return Ok(new FeatureFlagListResponse(flags, flags.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flags for subscription {SubscriptionId}", subscriptionId);
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }

    /// <summary>
    /// Gets a specific feature flag for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flagName">The feature flag name.</param>
    /// <returns>The feature flag if it exists.</returns>
    [HttpGet("{subscriptionId:guid}/{flagName}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(FeatureFlagValue), Description = "The feature flag")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "Feature flag not found")]
    [ValidateModelState]
    public async Task<IActionResult> GetFeatureFlag([FromRoute] Guid subscriptionId, [FromRoute] string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName))
        {
            return BadRequest(new FeatureFlagResponse(false, "Flag name cannot be empty"));
        }

        try
        {
            var flag = await _featureFlagService.GetFlagAsync(subscriptionId, flagName);
            if (flag == null)
            {
                return NotFound();
            }

            return Ok(flag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flag {FlagName} for subscription {SubscriptionId}",
                flagName, subscriptionId);
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }

    /// <summary>
    /// Removes a feature flag for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flagName">The feature flag name.</param>
    /// <returns>The result of the operation.</returns>
    [HttpDelete("{subscriptionId:guid}/{flagName}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Flag removal result")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "Feature flag not found")]
    [ValidateModelState]
    public async Task<IActionResult> RemoveFeatureFlag([FromRoute] Guid subscriptionId, [FromRoute] string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName))
        {
            return BadRequest(new FeatureFlagResponse(false, "Flag name cannot be empty"));
        }

        try
        {
            var removed = await _featureFlagService.RemoveFlagAsync(subscriptionId, flagName);
            if (!removed)
            {
                return NotFound();
            }

            return Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag {FlagName} for subscription {SubscriptionId}",
                flagName, subscriptionId);
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }

    /// <summary>
    /// Gets all feature flags across all subscriptions (admin operation).
    /// </summary>
    /// <returns>All feature flags in the system.</returns>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(FeatureFlagListResponse), Description = "All feature flags")]
    [ValidateModelState]
    public async Task<IActionResult> GetAllFeatureFlags()
    {
        try
        {
            var flags = await _featureFlagService.GetAllFlagsAsync();
            return Ok(new FeatureFlagListResponse(flags, flags.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all feature flags");
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }

    /// <summary>
    /// Gets the list of all available feature flags with their metadata.
    /// </summary>
    /// <returns>The list of available feature flag definitions.</returns>
    [HttpGet("available")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(AvailableFeatureFlagsResponse), Description = "Available feature flags")]
    [ValidateModelState]
    public IActionResult GetAvailableFeatureFlags()
    {
        try
        {
            var flags = FeatureFlags.AllFlags.Values.ToList();
            return Ok(new AvailableFeatureFlagsResponse(flags));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available feature flags");
            return StatusCode(500, new FeatureFlagResponse(false, "Internal server error"));
        }
    }
}
