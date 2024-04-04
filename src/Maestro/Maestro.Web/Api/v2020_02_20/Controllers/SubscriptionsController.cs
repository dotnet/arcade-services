// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Kusto.Cloud.Platform.Utils;
using Maestro.Data;
using Maestro.Web.Api.v2020_02_20.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Maestro.Web.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Create/Read/Update/Delete <see cref="Subscription"/>s
/// </summary>
[Route("subscriptions")]
[ApiVersion("2020-02-20")]
public class SubscriptionsController : v2019_01_16.Controllers.SubscriptionsController
{
    public const string RequiredOrgForSubscriptionNotification = "microsoft";

    private readonly BuildAssetRegistryContext _context;
    private readonly IGitHubClientFactory _gitHubClientFactory;

    public SubscriptionsController(
        BuildAssetRegistryContext context,
        IBackgroundQueue queue,
        IGitHubClientFactory gitHubClientFactory)
        : base(context, queue)
    {
        _context = context;
        _gitHubClientFactory = gitHubClientFactory;
    }

    [ApiRemoved]
    public sealed override IActionResult ListSubscriptions(
        string sourceRepository = null,
        string targetRepository = null,
        int? channelId = null,
        bool? enabled = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///   Gets a list of all <see cref="Subscription"/>s that match the given search criteria.
    /// </summary>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Subscription>), Description = "The list of Subscriptions")]
    [ValidateModelState]
    public IActionResult ListSubscriptions(
        string sourceRepository = null,
        string targetRepository = null,
        int? channelId = null,
        bool? enabled = null,
        bool? sourceEnabled = null,
        string sourceDirectory = null,
        string targetDirectory = null)
    {
        IQueryable<Data.Models.Subscription> query = _context.Subscriptions
            .Include(s => s.Channel)
            .Include(s => s.LastAppliedBuild)
            .Include(s => s.ExcludedAssets);

        if (!string.IsNullOrEmpty(sourceRepository))
        {
            query = query.Where(sub => sub.SourceRepository == sourceRepository);
        }

        if (!string.IsNullOrEmpty(targetRepository))
        {
            query = query.Where(sub => sub.TargetRepository == targetRepository);
        }

        if (channelId.HasValue)
        {
            query = query.Where(sub => sub.ChannelId == channelId.Value);
        }

        if (enabled.HasValue)
        {
            query = query.Where(sub => sub.Enabled == enabled.Value);
        }

        if (sourceEnabled.HasValue)
        {
            query = query.Where(sub => sub.SourceEnabled == sourceEnabled.Value);
        }

        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            query = query.Where(sub => sub.SourceDirectory == sourceDirectory);
        }

        if (!string.IsNullOrEmpty(targetDirectory))
        {
            query = query.Where(sub => sub.TargetDirectory == targetDirectory);
        }

        List<Subscription> results = query.AsEnumerable().Select(sub => new Subscription(sub)).ToList();
        return Ok(results);
    }

    /// <summary>
    ///   Gets a single <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/></param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "The requested Subscription")]
    [ValidateModelState]
    public override async Task<IActionResult> GetSubscription(Guid id)
    {
        Data.Models.Subscription subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
            .Include(sub => sub.Channel)
            .Include(sub => sub.LastAppliedBuild)
            .Include(sub => sub.ExcludedAssets)
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        return Ok(new Subscription(subscription));
    }

    /// <summary>
    ///   Trigger a <see cref="Subscription"/> manually by id
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to trigger.</param>
    /// <param name="buildId">'bar-build-id' if specified, a specific build is requested</param>
    [HttpPost("{id}/trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public override async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0)
    {
        return await TriggerSubscriptionCore(id, buildId);
    }

    [ApiRemoved]
    public sealed override Task<IActionResult> UpdateSubscription(Guid id, [FromBody] v2018_07_16.Models.SubscriptionUpdate update)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///   Edit an existing <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to update</param>
    /// <param name="update">An object containing the new data for the <see cref="Subscription"/></param>
    [HttpPatch("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully updated")]
    [ValidateModelState]
    public async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] SubscriptionUpdate update)
    {
        Data.Models.Subscription subscription = await _context.Subscriptions
            .Where(sub => sub.Id == id)
            .Include(sub => sub.Channel)
            .Include(sub => sub.ExcludedAssets)
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            return NotFound();
        }

        var doUpdate = false;

        if (!string.IsNullOrEmpty(update.SourceRepository))
        {
            subscription.SourceRepository = update.SourceRepository;
            doUpdate = true;
        }

        if (update.Policy != null)
        {
            subscription.PolicyObject = update.Policy.ToDb();
            doUpdate = true;
        }

        if (update.PullRequestFailureNotificationTags != null)
        {
            if (!await AllNotificationTagsValid(update.PullRequestFailureNotificationTags))
            {
                return BadRequest(new ApiError("Invalid value(s) provided in Pull Request Failure Notification Tags; is everyone listed publicly a member of the Microsoft github org?"));
            }

            subscription.PullRequestFailureNotificationTags = update.PullRequestFailureNotificationTags;
            doUpdate = true;
        }

        if (!string.IsNullOrEmpty(update.ChannelName))
        {
            Data.Models.Channel channel = await _context.Channels.Where(c => c.Name == update.ChannelName)
                .FirstOrDefaultAsync();
            if (channel == null)
            {
                return BadRequest(
                    new ApiError(
                        "The request is invalid",
                        new[] { $"The channel '{update.ChannelName}' could not be found." }));
            }

            subscription.Channel = channel;
            doUpdate = true;
        }

        if (update.Enabled.HasValue)
        {
            subscription.Enabled = update.Enabled.Value;
            doUpdate = true;
        }

        if (subscription.SourceEnabled && update.SourceDirectory == null && update.TargetDirectory == null)
        {
            return BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require the source or target directory to be set"));
        }

        if (update.SourceDirectory != null && update.TargetDirectory != null)
        {
            return BadRequest(new ApiError("The request is invalid. Only one of source or target directory can be set"));
        }

        if (update.SourceDirectory != subscription.SourceDirectory)
        {
            subscription.SourceDirectory = update.SourceDirectory;
            doUpdate = true;
        }

        if (update.TargetDirectory != subscription.TargetDirectory)
        {
            subscription.TargetDirectory = update.TargetDirectory;
            doUpdate = true;
        }

        if (update.SourceEnabled.HasValue)
        {
            // Turning off source-enabled will clear the source directory
            if (!update.SourceEnabled.Value)
            {
                if (subscription.SourceDirectory != null)
                {
                    subscription.SourceDirectory = null;
                    doUpdate = true;
                }

                if (subscription.TargetDirectory != null)
                {
                    subscription.TargetDirectory = null;
                    doUpdate = true;
                }
            }
            // Turning on source-enabled will require a source directory
            else if (subscription.SourceDirectory == null && subscription.TargetDirectory == null)
            {
                return BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require source or target directory to be set"));
            }

            subscription.SourceEnabled = update.SourceEnabled.Value;
            doUpdate = true;
        }

        update.ExcludedAssets = [.. (update.ExcludedAssets?.OrderBy(a => a).ToList() ?? [])];
        var currentSubscriptions = subscription.ExcludedAssets.Select(a => a.Filter).OrderBy(a => a);
        if (!currentSubscriptions.SequenceEqual(update.ExcludedAssets))
        {
            subscription.ExcludedAssets = update.ExcludedAssets.Select(asset => new Data.Models.AssetFilter() { Filter = asset }).ToList();
            doUpdate = true;
        }

        if (doUpdate)
        {
            Data.Models.Subscription equivalentSubscription = await FindEquivalentSubscription(subscription);
            if (equivalentSubscription != null)
            {
                return Conflict(
                    new ApiError(
                        "the request is invalid",
                        new[]
                        {
                            $"The subscription '{equivalentSubscription.Id}' already performs the same update."
                        }));
            }

            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
        }

        return Ok(new Subscription(subscription));
    }

    /// <summary>
    ///  Subscriptions support notifying GitHub tags when non-batched dependency flow PRs fail checks.
    ///  Before inserting them into the database, we'll make sure they're either not a user's login or
    ///  that user is publicly a member of the Microsoft organization so we can store their login.
    /// </summary>
    private async Task<bool> AllNotificationTagsValid(string pullRequestFailureNotificationTags)
    {
        string[] allTags = pullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries);

        // We'll only be checking public membership in the Microsoft org, so no token needed
        var client = _gitHubClientFactory.CreateGitHubClient(string.Empty);
        bool success = true;

        foreach (string tagToNotify in allTags)
        {
            // remove @ if it's there
            string tag = tagToNotify.TrimStart('@');

            try
            {
                IReadOnlyList<Octokit.Organization> orgList = await client.Organization.GetAllForUser(tag);
                success &= orgList.Any(o => o.Login?.Equals(RequiredOrgForSubscriptionNotification, StringComparison.InvariantCultureIgnoreCase) == true);
            }
            catch (Octokit.NotFoundException)
            {
                // Non-existent user: Either a typo, or a group (we don't have the admin privilege to find out, so just allow it)
            }
        }

        return success;
    }

    /// <summary>
    ///   Delete an existing <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to delete</param>
    [HttpDelete("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully deleted")]
    [ValidateModelState]
    public override async Task<IActionResult> DeleteSubscription(Guid id)
    {
        Data.Models.Subscription subscription = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        Data.Models.SubscriptionUpdate subscriptionUpdate =
            await _context.SubscriptionUpdates.FirstOrDefaultAsync(u => u.SubscriptionId == subscription.Id);

        if (subscriptionUpdate != null)
        {
            _context.SubscriptionUpdates.Remove(subscriptionUpdate);
        }

        _context.Subscriptions.Remove(subscription);

        await _context.SaveChangesAsync();
        return Ok(new Subscription(subscription));
    }

    [ApiRemoved]
    public override Task<IActionResult> Create([FromBody, Required] v2018_07_16.Models.SubscriptionData subscription)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///   Creates a new <see cref="Subscription"/>
    /// </summary>
    /// <param name="subscription">An object containing data for the new <see cref="Subscription"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Subscription), Description = "New Subscription successfully created")]
    [ValidateModelState]
    public async Task<IActionResult> Create([FromBody, Required] SubscriptionData subscription)
    {
        Data.Models.Channel channel = await _context.Channels.Where(c => c.Name == subscription.ChannelName)
            .FirstOrDefaultAsync();
        if (channel == null)
        {
            return BadRequest(
                new ApiError(
                    "the request is invalid",
                    new[] { $"The channel '{subscription.ChannelName}' could not be found." }));
        }

        Data.Models.Repository repo = await _context.Repositories.FindAsync(subscription.TargetRepository);

        if (subscription.TargetRepository.Contains("github.com"))
        {
            // If we have no repository information or an invalid installation id
            // then we will fail when trying to update things, so we fail early.
            if (repo == null || repo.InstallationId <= 0)
            {
                return BadRequest(
                    new ApiError(
                        "the request is invalid",
                        new[]
                        {
                            $"The repository '{subscription.TargetRepository}' does not have an associated github installation. " +
                            "The Maestro github application must be installed by the repository's owner and given access to the repository."
                        }));
            }
        }

        if (subscription.SourceEnabled.HasValue)
        {
            if (subscription.SourceEnabled.Value && subscription.SourceDirectory == null && subscription.TargetDirectory == null)
            {
                return BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require the source or target directory to be set"));
            }

            if (!subscription.SourceEnabled.Value && (subscription.SourceDirectory ?? subscription.TargetDirectory) != null)
            {
                return BadRequest(new ApiError("The request is invalid. Source or target directory can be set only for source-enabled subscriptions"));
            }

            if (subscription.SourceDirectory != null && subscription.TargetDirectory != null)
            {
                return BadRequest(new ApiError("The request is invalid. Only one of source or target directory can be set"));
            }
        }

        // In the case of a dev.azure.com repository, we don't have an app installation,
        // but we should add an entry in the repositories table, as this is required when
        // adding a new subscription policy.
        // NOTE:
        // There is a good chance here that we will need to also handle <account>.visualstudio.com
        // but leaving it out for now as it would be preferred to use the new format
        else if (subscription.TargetRepository.Contains("dev.azure.com"))
        {
            if (repo == null)
            {
                _context.Repositories.Add(
                    new Data.Models.Repository
                    {
                        RepositoryName = subscription.TargetRepository,
                        InstallationId = default
                    });
            }
        }

        Data.Models.Subscription subscriptionModel = subscription.ToDb();
        subscriptionModel.Channel = channel;

        // Check that we're not about add an existing subscription that is identical
        Data.Models.Subscription equivalentSubscription = await FindEquivalentSubscription(subscriptionModel);
        if (equivalentSubscription != null)
        {
            return BadRequest(
                new ApiError(
                    "the request is invalid",
                    new[]
                    {
                        $"The subscription '{equivalentSubscription.Id}' already performs the same update."
                    }));
        }

        if (!string.IsNullOrEmpty(subscriptionModel.PullRequestFailureNotificationTags))
        {
            if (!await AllNotificationTagsValid(subscriptionModel.PullRequestFailureNotificationTags))
            {
                return BadRequest(new ApiError("Invalid value(s) provided in Pull Request Failure Notification Tags; is everyone listed publicly a member of the Microsoft github org?"));
            }
        }

        await _context.Subscriptions.AddAsync(subscriptionModel);
        await _context.SaveChangesAsync();
        return CreatedAtRoute(
            new
            {
                action = "GetSubscription",
                id = subscriptionModel.Id
            },
            new Subscription(subscriptionModel));
    }

    /// <summary>
    ///     Find an existing subscription in the database with the same key data as the subscription we are adding/updating
    ///     
    ///     This should be called before updating or adding new subscriptions to the database
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Subscription if it is found, null otherwise</returns>
    private async Task<Data.Models.Subscription> FindEquivalentSubscription(Data.Models.Subscription updatedOrNewSubscription) =>
        // Compare subscriptions based on key elements and a different id
        await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceRepository == updatedOrNewSubscription.SourceRepository
                && sub.ChannelId == updatedOrNewSubscription.Channel.Id
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.SourceEnabled == updatedOrNewSubscription.SourceEnabled
                && sub.SourceDirectory == updatedOrNewSubscription.SourceDirectory
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);
}
