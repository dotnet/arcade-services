// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class SubscriptionPolicy
{
    public SubscriptionPolicy()
    {
    }

    public SubscriptionPolicy(Maestro.Data.Models.SubscriptionPolicy other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Batchable = other.Batchable;
        UpdateFrequency = (UpdateFrequency)(int)other.UpdateFrequency;
    }

    public bool Batchable { get; set; } = false;

    [Required]
    public UpdateFrequency UpdateFrequency { get; set; }

    public Maestro.Data.Models.SubscriptionPolicy ToDb()
    {
        return new Maestro.Data.Models.SubscriptionPolicy
        {
            Batchable = Batchable,
            UpdateFrequency = (Maestro.Data.Models.UpdateFrequency)(int)UpdateFrequency
        };
    }
}
