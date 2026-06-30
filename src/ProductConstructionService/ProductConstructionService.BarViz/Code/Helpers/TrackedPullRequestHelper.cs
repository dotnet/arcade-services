// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.FluentUI.AspNetCore.Components.Extensions;

namespace ProductConstructionService.BarViz.Code.Helpers;

public static class TrackedPullRequestHelper
{
    public static string GetPullRequestAge(this TrackedPullRequest pullRequest)
    {
        if (pullRequest.CreationDate == default)
        {
            return "N/A";
        }

        return (DateTime.UtcNow - pullRequest.CreationDate).ToTimeAgo();
    }
}
