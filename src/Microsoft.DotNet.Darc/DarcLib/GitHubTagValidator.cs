// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Octokit;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Validates GitHub user tags for subscription failure notifications.
/// </summary>
public interface IGitHubTagValidator
{
    /// <summary>
    /// Validates a single notification tag by checking if the user is publicly a member of the Microsoft organization.
    /// </summary>
    /// <param name="tag">The GitHub username (without the @ prefix).</param>
    /// <returns>True if the tag is valid (user is in Microsoft org or doesn't exist), false otherwise.</returns>
    Task<bool> IsNotificationTagValidAsync(string tag);
}

/// <summary>
/// Validates GitHub user tags for subscription failure notifications.
/// Checks if users are publicly members of the Microsoft organization.
/// </summary>
public class GitHubTagValidator : IGitHubTagValidator
{
    private const string RequiredOrgForSubscriptionNotification = "microsoft";

    private readonly IGitHubClient _client;

    public GitHubTagValidator(IGitHubClientFactory gitHubClientFactory)
    {
        // We'll only be checking public membership in the Microsoft org, so no token needed
        _client = gitHubClientFactory.CreateGitHubClient(string.Empty);
    }

    /// <inheritdoc/>
    public async Task<bool> IsNotificationTagValidAsync(string tag)
    {
        try
        {
            var orgList = await _client.Organization.GetAllForUser(tag);
            return orgList.Any(o => o.Login?.Equals(RequiredOrgForSubscriptionNotification, StringComparison.InvariantCultureIgnoreCase) == true);
        }
        catch (NotFoundException)
        {
            // Non-existent user: Either a typo, or a group (we don't have the admin privilege to find out, so just allow it)
            return true;
        }
    }
}
