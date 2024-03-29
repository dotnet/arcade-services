﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;

namespace SubscriptionActorService;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> for batched subscriptions that reads its Target and Merge Policies
///     from the configuration for a repository
/// </summary>
internal class BatchedPullRequestActorImplementation : PullRequestActorImplementation
{
    private readonly ActorId _id;
    private readonly BuildAssetRegistryContext _context;

    public BatchedPullRequestActorImplementation(
        ActorId id,
        IReminderManager reminders,
        IActorStateManager stateManager,
        IMergePolicyEvaluator mergePolicyEvaluator,
        ICoherencyUpdateResolver updateResolver,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestBuilder pullRequestBuilder,
        ILoggerFactory loggerFactory,
        IActionRunner actionRunner,
        IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory)
        : base(
            reminders,
            stateManager,
            mergePolicyEvaluator,
            updateResolver,
            context,
            remoteFactory,
            pullRequestBuilder,
            loggerFactory,
            actionRunner,
            subscriptionActorFactory)
    {
        _id = id;
        _context = context;
    }

    private (string repository, string branch) Target => PullRequestActorId.Parse(_id);

    protected override Task<(string repository, string branch)> GetTargetAsync()
    {
        return Task.FromResult((Target.repository, Target.branch));
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        RepositoryBranch repositoryBranch =
            await _context.RepositoryBranches.FindAsync(Target.repository, Target.branch);
        return (IReadOnlyList<MergePolicyDefinition>) repositoryBranch?.PolicyObject?.MergePolicies ??
               Array.Empty<MergePolicyDefinition>();
    }
}
