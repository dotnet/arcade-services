﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.Api.Queue.Jobs;

[JsonDerivedType(typeof(TextJob), typeDiscriminator: nameof(TextJob))]
[JsonDerivedType(typeof(CodeFlowJob), typeDiscriminator: nameof(CodeFlowJob))]
internal abstract class Job
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract string Type { get; }
}
