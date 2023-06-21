// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;

namespace Maestro.Contracts;

public interface IPullRequestActor : IActor
{
    Task<string> RunActionAsync(string method, string arguments);
    Task UpdateAssetsAsync(Guid subscriptionId, int buildId, string sourceRepo, string sourceSha, List<Asset> assets);
}
