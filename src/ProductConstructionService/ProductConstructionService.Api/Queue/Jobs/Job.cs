// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.Api.Queue.Jobs;

[JsonDerivedType(typeof(TextJob), typeDiscriminator: nameof(TextJob))]
public abstract class Job
{
    public required Guid Id { get; init; }
}
