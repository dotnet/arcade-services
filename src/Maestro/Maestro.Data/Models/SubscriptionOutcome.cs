// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Data.Models;

public class SubscriptionOutcome
{
    public string OperationId { get; set; }

    public Guid SubscriptionId { get; set; }

    public int BuildId { get; set; }

    public DateTime Date { get; set;  }

    public string OutcomeMessage { get; set; }

    public OutcomeType OutcomeType { get; set; }
}

public enum OutcomeType
{
    Fail,
    Success,
    NoUpdate
}
