// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models;
public class PullRequestReviews : IGithubEtagResource
{
    public IEnumerable<GithubReview> Reviews { get; set; }
    public string Etag { get; set; }

}
