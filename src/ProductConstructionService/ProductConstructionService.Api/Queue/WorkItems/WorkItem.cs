// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.Api.Queue.WorkItems;

[JsonDerivedType(typeof(TextWorkitem), typeDiscriminator: nameof(TextWorkitem))]
public abstract class WorkItem
{
    public Guid Id { get; } = Guid.NewGuid();
}
