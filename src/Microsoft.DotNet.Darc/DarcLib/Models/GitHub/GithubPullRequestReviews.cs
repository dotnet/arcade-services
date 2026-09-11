// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models.GitHub;

namespace Microsoft.DotNet.DarcLib.Models;
public class GithubPullRequestReviews : IGithubEtagResource
{
    public IReadOnlyCollection<GithubReview> Reviews { get; set; }
    public string Etag { get; set; }

}
