// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobRunners;

public interface IJobRunner
{
    Task RunAsync(Job job, CancellationToken cancellationToken);
}
