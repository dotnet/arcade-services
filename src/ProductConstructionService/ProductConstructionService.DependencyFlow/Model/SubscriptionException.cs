// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.Model;

/// <summary>
///     Exception thrown when there is a failure updating a subscription that should be surfaced to users.
/// </summary>
internal class SubscriptionException : Exception
{
    public SubscriptionException(string message) : base(message)
    {
    }

    public SubscriptionException() : this("There was a problem updating the subscription")
    {
    }

    public SubscriptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
