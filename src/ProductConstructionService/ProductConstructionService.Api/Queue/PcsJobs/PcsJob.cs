﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.Api.Queue.WorkItems;

[JsonDerivedType(typeof(TextPcsJob), typeDiscriminator: nameof(TextPcsJob))]
public abstract class PcsJob
{
    public PcsJob(Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
    }

    public Guid Id { get; }
}
