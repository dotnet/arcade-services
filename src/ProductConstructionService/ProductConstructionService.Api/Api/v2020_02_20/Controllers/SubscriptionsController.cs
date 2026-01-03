// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductConstructionService.Api.v2020_02_20.Models;
using ProductConstructionService.Common.CodeflowHistory;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

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
        IGitHubClientFactory gitHubClientFactory,
        IGitHubInstallationIdResolver gitHubInstallationRetriever,
        IWorkItemProducerFactory workItemProducerFactory,
        IOptions<EnvironmentNamespaceOptions> environmentNamespaceOptions,
        IRemoteFactory remoteFactory,
        ICodeflowHistoryManager codeflowHistoryManager,
        ILogger<SubscriptionsController> logger)
        : base(context, workItemProducerFactory, gitHubInstallationRetriever, remoteFactory, codeflowHistoryManager, environmentNamespaceOptions, logger)
    {
        _context = context;
        _gitHubClientFactory = gitHubClientFactory;
    }

    [ApiRemoved]
    public sealed override IActionResult ListSubscriptions(
        string? sourceRepository = null,
        string? targetRepository = null,
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
        string? sourceRepository = null,
        string? targetRepository = null,
        int? channelId = null,
        bool? enabled = null,
        bool? sourceEnabled = null,
        string? sourceDirectory = null,
        string? targetDirectory = null)
    {
        IQueryable<Maestro.Data.Models.Subscription> query = _context.Subscriptions
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

        List<Subscription> results = [.. query.AsEnumerable().Select(sub => new Subscription(sub))];
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
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
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
    /// <param name="force">'force' if specified, force update even for PRs with pending or successful checks</param>
    [HttpPost("{id}/trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public override async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0, [FromQuery(Name = "force")] bool force = false)
    {
        return await TriggerSubscriptionCore(id, buildId, force);
    }

    [HttpGet("{id}/codeflowhistory")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public override async Task<IActionResult> GetCodeflowHistory(Guid id)
    {
        return await GetCodeflowHistoryCore(id);
    }

    [ApiRemoved]
    public sealed override Task<IActionResult> UpdateSubscription(Guid id, [FromBody] ProductConstructionService.Api.v2018_07_16.Models.SubscriptionUpdate update)
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
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions
            .Where(sub => sub.Id == id)
            .Include(sub => sub.Channel)
            .Include(sub => sub.ExcludedAssets)
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            return NotFound();
        }

        // Check if the update would result in an invalid batched codeflow subscription
        // Calculate the final state considering both current subscription state and update values
        bool finalSourceEnabled = update.SourceEnabled ?? subscription.SourceEnabled;
        bool finalBatchable = update.Policy?.Batchable ?? subscription.PolicyObject.Batchable;
        
        if (finalSourceEnabled && finalBatchable)
        {
            return BadRequest(new ApiError("The request is invalid. Batched codeflow subscriptions are not supported."));
        }

        // Subscriptions are not allowed to change their sourceEnabled setting
        if (subscription.SourceEnabled != (update.SourceEnabled ?? false))
        {
            return BadRequest(new ApiError("The request is invalid. Subscriptions are not allowed to change their sourceEnabled setting."));
        }

        if (update.SourceEnabled == true && string.IsNullOrEmpty(update.TargetDirectory) && string.IsNullOrEmpty(update.SourceDirectory))
        {
            return base.BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require the source or target directory to be set"));
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
            Maestro.Data.Models.Channel? channel = await _context.Channels.Where(c => c.Name == update.ChannelName)
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

        if (subscription.SourceEnabled && update.SourceEnabled.HasValue && update.SourceEnabled.Value && string.IsNullOrEmpty(update.SourceDirectory) && string.IsNullOrEmpty(update.TargetDirectory))
        {
            return BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require the source or target directory to be set"));
        }

        if (!string.IsNullOrEmpty(update.SourceDirectory) && !string.IsNullOrEmpty(update.TargetDirectory))
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
                if (!string.IsNullOrEmpty(subscription.SourceDirectory))
                {
                    subscription.SourceDirectory = null;
                    doUpdate = true;
                }
            }
            // Turning on source-enabled will require a source directory
            else if (string.IsNullOrEmpty(subscription.SourceDirectory) && string.IsNullOrEmpty(subscription.TargetDirectory))
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
            subscription.ExcludedAssets = [.. update.ExcludedAssets.Select(asset => new Maestro.Data.Models.AssetFilter() { Filter = asset })];
            doUpdate = true;
        }

        if (doUpdate)
        {
            Maestro.Data.Models.Subscription? equivalentSubscription = await FindEquivalentSubscription(subscription);
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

            // Check for codeflow subscription conflicts
            var conflictError = await ValidateCodeflowSubscriptionConflicts(subscription);
            if (conflictError != null)
            {
                return Conflict(new ApiError("the request is invalid", new[] { conflictError }));
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
        var allTags = pullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries);

        // We'll only be checking public membership in the Microsoft org, so no token needed
        var client = _gitHubClientFactory.CreateGitHubClient(string.Empty);
        var success = true;

        foreach (var tagToNotify in allTags)
        {
            // remove @ if it's there
            var tag = tagToNotify.TrimStart('@');

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
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        Maestro.Data.Models.SubscriptionUpdate? subscriptionUpdate =
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
    public override Task<IActionResult> Create([FromBody, Required] ProductConstructionService.Api.v2018_07_16.Models.SubscriptionData subscription)
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
        Maestro.Data.Models.Channel? channel = await _context.Channels
            .Where(c => c.Name == subscription.ChannelName)
            .FirstOrDefaultAsync();

        if (channel == null)
        {
            return BadRequest(new ApiError("The request is invalid", [$"The channel '{subscription.ChannelName}' could not be found."]));
        }

        if (!await EnsureRepositoryRegistration(subscription.TargetRepository))
        {
            return BadRequest(new ApiError("The request is invalid",
            [
                $"No Maestro GitHub application installation found for repository '{subscription.TargetRepository}'. " +
                "The Maestro github application must be installed by the repository's owner and given access to the repository."
            ]));
        }

        if (subscription.SourceEnabled.HasValue)
        {
            if (subscription.SourceEnabled.Value && string.IsNullOrEmpty(subscription.SourceDirectory) && string.IsNullOrEmpty(subscription.TargetDirectory))
            {
                return BadRequest(new ApiError("The request is invalid. Source-enabled subscriptions require the source or target directory to be set"));
            }

            if (!subscription.SourceEnabled.Value && !string.IsNullOrEmpty(subscription.SourceDirectory))
            {
                return BadRequest(new ApiError("The request is invalid. Source directory can be set only for source-enabled subscriptions"));
            }

            if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
            {
                return BadRequest(new ApiError("The request is invalid. Only one of source or target directory can be set"));
            }

            if (subscription.Policy.Batchable && subscription.SourceEnabled.Value)
            {
                return BadRequest(new ApiError("The request is invalid. Batched codeflow subscriptions are not supported."));
            }
        }

        Maestro.Data.Models.Subscription subscriptionModel = subscription.ToDb();
        subscriptionModel.Channel = channel;
        subscriptionModel.Id = Guid.NewGuid();
        // set the Namespace to the default value for the environment we're running in
        subscriptionModel.Namespace = await _context.Namespaces.SingleAsync(n => n.Name == _environmentNamespaceOptions.Value.DefaultNamespaceName);

        // Check that we're not about add an existing subscription that is identical
        Maestro.Data.Models.Subscription? equivalentSubscription = await FindEquivalentSubscription(subscriptionModel);
        if (equivalentSubscription != null)
        {
            return BadRequest(
                new ApiError(
                    "The request is invalid",
                    new[]
                    {
                        $"The subscription '{equivalentSubscription.Id}' already performs the same update."
                    }));
        }

        // Check for codeflow subscription conflicts
        var conflictError = await ValidateCodeflowSubscriptionConflicts(subscriptionModel);
        if (conflictError != null)
        {
            return BadRequest(new ApiError("The request is invalid", new[] { conflictError }));
        }

        if (!string.IsNullOrEmpty(subscriptionModel.PullRequestFailureNotificationTags)
            && !await AllNotificationTagsValid(subscriptionModel.PullRequestFailureNotificationTags))
        {
            return BadRequest(new ApiError(
                "Invalid value(s) provided in Pull Request Failure Notification Tags; " +
                "is everyone listed publicly a member of the Microsoft github org?"));
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
    ///     Validates codeflow subscription conflicts
    /// </summary>
    /// <param name="subscription">Subscription to validate</param>
    /// <returns>Error message if conflict found, null if no conflicts</returns>
    private async Task<string?> ValidateCodeflowSubscriptionConflicts(Maestro.Data.Models.Subscription subscription)
    {
        if (!subscription.SourceEnabled)
        {
            return null;
        }

        // Check for backflow conflicts (source directory not empty)
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            var conflictingBackflowSubscription = await FindConflictingBackflowSubscription(subscription);
            if (conflictingBackflowSubscription != null)
            {
                return $"A backflow subscription '{conflictingBackflowSubscription.Id}' already exists for the same target repository and branch. " +
                       "Only one backflow subscription is allowed per target repository and branch combination.";
            }
        }

        // Check for forward flow conflicts (target directory not empty)
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            var conflictingForwardFlowSubscription = await FindConflictingForwardFlowSubscription(subscription);
            if (conflictingForwardFlowSubscription != null)
            {
                return $"A forward flow subscription '{conflictingForwardFlowSubscription.Id}' already exists for the same VMR repository, branch, and target directory. " +
                       "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.";
            }
        }

        return null;
    }

    /// <summary>
    ///     Find an existing subscription in the database with the same key data as the subscription we are adding/updating
    ///     
    ///     This should be called before updating or adding new subscriptions to the database
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Subscription if it is found, null otherwise</returns>
    private async Task<Maestro.Data.Models.Subscription?> FindEquivalentSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription) =>
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

    /// <summary>
    ///     Find a conflicting backflow subscription (different subscription targeting same repo/branch)
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Conflicting subscription if found, null otherwise</returns>
    private async Task<Maestro.Data.Models.Subscription?> FindConflictingBackflowSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription) =>
        await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceEnabled == true
                && !string.IsNullOrEmpty(sub.SourceDirectory) // Backflow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.Id != updatedOrNewSubscription.Id);

    /// <summary>
    ///     Find a conflicting forward flow subscription (different subscription targeting same VMR branch/directory)
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Conflicting subscription if found, null otherwise</returns>
    private async Task<Maestro.Data.Models.Subscription?> FindConflictingForwardFlowSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription) =>
        await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceEnabled == true
                && !string.IsNullOrEmpty(sub.TargetDirectory) // Forward flow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);
}
