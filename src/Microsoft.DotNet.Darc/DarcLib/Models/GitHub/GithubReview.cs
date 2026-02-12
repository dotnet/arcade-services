// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib.Models.GitHub;

public class GithubReview : Review
{
    public GithubReview(ReviewState state, string url, string user, DateTimeOffset submittedAt)
        : base(state, url)
    {
        User = user;
        SubmittedAt = submittedAt;
    }

    /// <summary>
    ///     The user who submitted the review.
    /// </summary>
    public string User { get; set; }

    /// <summary>
    /// The date and time when the review was submitted.
    /// </summary>
    public DateTimeOffset SubmittedAt { get; private set; }
}
