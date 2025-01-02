// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Mvc;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class SubscriptionHistoryItem
{
    public SubscriptionHistoryItem(SubscriptionUpdateHistoryEntry other, IUrlHelper url, HttpContext context)
    {
        SubscriptionId = other.SubscriptionId;
        Action = other.Action;
        Success = other.Success;
        ErrorMessage = other.ErrorMessage;
        Timestamp = DateTime.SpecifyKind(other.Timestamp, DateTimeKind.Utc);
        if (!other.Success)
        {
            RetryUrl = new UriBuilder
            {
                Scheme = "https",
                Host = context.Request.GetUri().Host,
                Path = url.Action(
                    "RetrySubscriptionActionAsync",
                    "Subscriptions",
                    new
                    {
                        id = other.SubscriptionId,
                        timestamp = Timestamp.ToUnixTimeSeconds()
                    })
            }.Uri.AbsoluteUri;
        }
    }

    public DateTimeOffset Timestamp { get; }

    public string ErrorMessage { get; }

    public bool Success { get; }

    public Guid SubscriptionId { get; }

    public string Action { get; }

    public string RetryUrl { get; }
}
