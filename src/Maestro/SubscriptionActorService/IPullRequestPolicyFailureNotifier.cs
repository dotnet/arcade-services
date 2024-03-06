// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Threading.Tasks;
using SubscriptionActorService.StateModel;

namespace SubscriptionActorService;

public interface IPullRequestPolicyFailureNotifier
{
    Task TagSourceRepositoryGitHubContactsAsync(InProgressPullRequest pr);
}
