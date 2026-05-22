// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

namespace ProductConstructionService.Api.v2020_02_20.Models;

public class SubscriptionTriggerOutcome
{
    public SubscriptionTriggerOutcome(Maestro.Data.Models.SubscriptionOutcome other)
    {
        ArgumentNullException.ThrowIfNull(other);

        OperationId = other.OperationId;
        SubscriptionId = other.SubscriptionId;
        BuildId = other.BuildId;
        Date = other.Date;
        Message = other.Message;
        Type = (OutcomeType)other.Type;
    }

    public string OperationId { get; }

    public Guid SubscriptionId { get; }

    public int BuildId { get; }

    public DateTimeOffset Date { get; }

    public string Message { get; }

    public OutcomeType Type { get; }
}

public enum OutcomeType
{
    Updated,
    NoUpdate,
    NotUpdatable,
    Failure,
    UserError,
    HasConflict,
    Rescheduled,
}
