// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.UserSentiment;

/// <summary>
///     Sentiment feature ID, integer values MUST remain stable,
///     as they are serialized as integers into the sentiment feedback system
/// </summary>
public enum SentimentFeature
{
    DeveloperWorkflowGitHubComment = 1,
    DeveloperWorkflowGitHubCheckTab = 2,
    AzureDevOpsHelixExtensionTab = 3,
    HelixQueueInsights = 4,
}
