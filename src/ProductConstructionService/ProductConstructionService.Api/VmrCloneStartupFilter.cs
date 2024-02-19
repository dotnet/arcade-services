// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api;

public class VmrCloneStartupFilter(RepositoryCloneManager repositoryCloneManager, IVmrInfo vmrInfo) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            // If Vmr cloning is taking more than an hour, something is wrong
            CancellationTokenSource tokenSource = new(TimeSpan.FromHours(1));
            ILocalGitRepo repo = repositoryCloneManager.PrepareVmrCloneAsync(tokenSource.Token).GetAwaiter().GetResult();
            tokenSource.Token.ThrowIfCancellationRequested();

            vmrInfo.VmrPath = repo.Path;

            next(builder);
        };
    }
}
