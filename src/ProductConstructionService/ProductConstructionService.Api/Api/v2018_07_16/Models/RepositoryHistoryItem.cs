// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Mvc;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class RepositoryHistoryItem
{
    public RepositoryHistoryItem(RepositoryBranchUpdateHistoryEntry other, IUrlHelper url, HttpContext context)
    {
        RepositoryName = other.Repository;
        BranchName = other.Branch;
        Timestamp = DateTime.SpecifyKind(other.Timestamp, DateTimeKind.Utc);
        ErrorMessage = other.ErrorMessage;
        Success = other.Success;
        Action = other.Action;
        if (!other.Success)
        {
            var pathAndQuery = url.Action(
                "RetryActionAsync",
                "Repository",
                new
                {
                    repository = other.Repository,
                    branch = other.Branch,
                    timestamp = Timestamp.ToUnixTimeSeconds()
                });
            (var path, var query) = Split2(pathAndQuery, '?');
            RetryUrl = new UriBuilder
            {
                Scheme = "https",
                Host = context.Request.GetUri().Host,
                Path = path,
                Query = query
            }.Uri.AbsoluteUri;
        }
    }

    public string RepositoryName { get; }

    public string BranchName { get; }

    public DateTimeOffset Timestamp { get; }

    public string ErrorMessage { get; }

    public bool Success { get; }

    public string Action { get; }

    public string RetryUrl { get; }

    private static (string left, string right) Split2(string value, char splitOn)
    {
        var idx = value.IndexOf(splitOn);

        if (idx < 0)
        {
            return (value, value.Substring(0, 0));
        }

        return (value.Substring(0, idx), value.Substring(idx + 1));
    }
}
