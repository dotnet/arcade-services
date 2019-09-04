// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.ServiceFabric.Actors;
using Channel = Maestro.Data.Models.Channel;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Update/Delete <see cref="Subscription"/>s
    /// </summary>
    [Route("subscriptions")]
    [ApiVersion("2018-07-16")]
    public class SubscriptionsController : Controller
    {
        private readonly BuildAssetRegistryContext _context;
        private readonly BackgroundQueue _queue;
        private readonly IDependencyUpdater _dependencyUpdater;
        private readonly Func<ActorId, ISubscriptionActor> _subscriptionActorFactory;

        public SubscriptionsController(
            BuildAssetRegistryContext context,
            BackgroundQueue queue,
            IDependencyUpdater dependencyUpdater,
            Func<ActorId, ISubscriptionActor> subscriptionActorFactory)
        {
            _context = context;
            _queue = queue;
            _dependencyUpdater = dependencyUpdater;
            _subscriptionActorFactory = subscriptionActorFactory;
        }

        /// <summary>
        ///   Gets a list of all <see cref="Subscription"/>s that match the given search criteria.
        /// </summary>
        /// <param name="sourceRepository"></param>
        /// <param name="targetRepository"></param>
        /// <param name="channelId"></param>
        /// <param name="enabled"></param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Subscription>), Description = "The list of Subscriptions")]
        [ValidateModelState]
        public virtual IActionResult ListSubscriptions(
            string sourceRepository = null,
            string targetRepository = null,
            int? channelId = null,
            bool? enabled = null)
        {
            IQueryable<Data.Models.Subscription> query = _context.Subscriptions.Include(s => s.Channel);

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
        public virtual async Task<IActionResult> GetSubscription(Guid id)
        {
            Data.Models.Subscription subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
                .Include(sub => sub.Channel)
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
        [HttpPost("{id}/trigger")]
        [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
        [ValidateModelState]
        public virtual async Task<IActionResult> TriggerSubscription(Guid id)
        {
            Data.Models.Subscription subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
                .Include(sub => sub.Channel)
                .FirstOrDefaultAsync(sub => sub.Id == id);

            if (subscription == null)
            {
                return NotFound();
            }

            _queue.Post(
                async () =>
                {
                    await _dependencyUpdater.StartSubscriptionUpdateAsync(id);
                });

            return Accepted(new Subscription(subscription));
        }

        /// <summary>
        ///   Trigger daily update
        /// </summary>
        [HttpPost("triggerDaily")]
        [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Trigger all subscriptions normally updated daily.")]
        [ValidateModelState]
        public virtual IActionResult TriggerDailyUpdate()
        {
            _queue.Post(
                async () =>
                {
                    await _dependencyUpdater.CheckDailySubscriptionsAsync(CancellationToken.None);
                });

            return Accepted();
        }

        /// <summary>
        ///   Edit an existing <see cref="Subscription"/>
        /// </summary>
        /// <param name="id">The id of the <see cref="Subscription"/> to update</param>
        /// <param name="update">An object containing the new data for the <see cref="Subscription"/></param>
        [HttpPatch("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully updated")]
        [ValidateModelState]
        public virtual async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] SubscriptionUpdate update)
        {
            Data.Models.Subscription subscription = await _context.Subscriptions.Where(sub => sub.Id == id)
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

            if (!string.IsNullOrEmpty(update.ChannelName))
            {
                Channel channel = await _context.Channels.Where(c => c.Name == update.ChannelName)
                    .FirstOrDefaultAsync();
                if (channel == null)
                {
                    return BadRequest(
                        new ApiError(
                            "The request is invalid",
                            new[] {$"The channel '{update.ChannelName}' could not be found."}));
                }

                subscription.Channel = channel;
                doUpdate = true;
            }

            if (update.Enabled.HasValue)
            {
                subscription.Enabled = update.Enabled.Value;
                doUpdate = true;
            }

            if (doUpdate)
            {
                Data.Models.Subscription equivalentSubscription = await FindEquivalentSubscription(subscription);
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

                _context.Subscriptions.Update(subscription);
                await _context.SaveChangesAsync();
            }

            return Ok(new Subscription(subscription));
        }

        /// <summary>
        ///   Delete an existing <see cref="Subscription"/>
        /// </summary>
        /// <param name="id">The id of the <see cref="Subscription"/> to delete</param>
        [HttpDelete("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully deleted")]
        [ValidateModelState]
        public virtual async Task<IActionResult> DeleteSubscription(Guid id)
        {
            Data.Models.Subscription subscription =
                await _context.Subscriptions.FirstOrDefaultAsync(sub => sub.Id == id);

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

        /// <summary>
        ///   Gets a paginated list of the Subscription history for the given Subscription
        /// </summary>
        /// <param name="id">The id of the <see cref="Subscription"/> to get history for</param>
        [HttpGet("{id}/history")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SubscriptionHistoryItem>), Description = "The list of Subscription history")]
        [Paginated(typeof(SubscriptionHistoryItem))]
        public virtual async Task<IActionResult> GetSubscriptionHistory(Guid id)
        {
            Data.Models.Subscription subscription = await _context.Subscriptions.Where(sub => sub.Id == id)
                .FirstOrDefaultAsync();

            if (subscription == null)
            {
                return NotFound();
            }

            IOrderedQueryable<SubscriptionUpdateHistoryEntry> query = _context.SubscriptionUpdateHistory
                .Where(u => u.SubscriptionId == id)
                .OrderByDescending(u => u.Timestamp);

            return Ok(query);
        }

        /// <summary>
        ///   Requests that Maestro++ retry the reference history item.
        ///   Links to this api are returned from the <see cref="GetSubscriptionHistory"/> api.
        /// </summary>
        /// <param name="id">The id of the <see cref="Subscription"/> containing the history item to retry</param>
        /// <param name="timestamp">The timestamp identifying the history item to retry</param>
        [HttpPost("{id}/retry/{timestamp}")]
        [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Retry successfully requested")]
        [SwaggerApiResponse(HttpStatusCode.NotAcceptable, Description = "The requested history item was successful and cannot be retried")]
        public virtual async Task<IActionResult> RetrySubscriptionActionAsync(Guid id, long timestamp)
        {
            DateTime ts = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

            Data.Models.Subscription subscription = await _context.Subscriptions.Where(sub => sub.Id == id)
                .FirstOrDefaultAsync();

            if (subscription == null)
            {
                return NotFound();
            }

            SubscriptionUpdateHistoryEntry update = await _context.SubscriptionUpdateHistory
                .Where(u => u.SubscriptionId == id)
                .FirstOrDefaultAsync(u => Math.Abs(EF.Functions.DateDiffSecond(u.Timestamp, ts)) < 1);

            if (update == null)
            {
                return NotFound();
            }

            if (update.Success)
            {
                return StatusCode(
                    (int) HttpStatusCode.NotAcceptable,
                    new ApiError("That action was successful, it cannot be retried."));
            }

            _queue.Post(
                async () =>
                {
                    ISubscriptionActor actor = _subscriptionActorFactory(new ActorId(subscription.Id));
                    await actor.RunActionAsync(update.Method, update.Arguments);
                });

            return Accepted();
        }

        /// <summary>
        ///   Creates a new <see cref="Subscription"/>
        /// </summary>
        /// <param name="subscription">An object containing data for the new <see cref="Subscription"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Subscription), Description = "New Subscription successfully created")]
        [ValidateModelState]
        public virtual async Task<IActionResult> Create([FromBody, Required] SubscriptionData subscription)
        {
            Channel channel = await _context.Channels.Where(c => c.Name == subscription.ChannelName)
                .FirstOrDefaultAsync();
            if (channel == null)
            {
                return BadRequest(
                    new ApiError(
                        "the request is invalid",
                        new[] {$"The channel '{subscription.ChannelName}' could not be found."}));
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

            Data.Models.Subscription equivalentSubscription = await FindEquivalentSubscription(subscriptionModel);
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
        private async Task<Data.Models.Subscription> FindEquivalentSubscription(Data.Models.Subscription updatedOrNewSubscription)
        {
            // Compare subscriptions based on the 4 key elements:
            // - Channel
            // - Source repo
            // - Target repo
            // - Target branch
            // - Not the same subscription id (for updates)
            return await _context.Subscriptions.FirstOrDefaultAsync(sub =>
                sub.SourceRepository.Equals(updatedOrNewSubscription.SourceRepository, StringComparison.OrdinalIgnoreCase) &&
                sub.ChannelId == updatedOrNewSubscription.Channel.Id &&
                sub.TargetRepository.Equals(updatedOrNewSubscription.TargetRepository, StringComparison.OrdinalIgnoreCase) &&
                sub.TargetBranch.Equals(updatedOrNewSubscription.TargetBranch, StringComparison.OrdinalIgnoreCase) &&
                sub.Id != updatedOrNewSubscription.Id);
        }
    }
}
