﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobProcessors;

public interface IJobProcessor
{
    Task ProcessJobAsync(Job job, CancellationToken cancellationToken);
}
