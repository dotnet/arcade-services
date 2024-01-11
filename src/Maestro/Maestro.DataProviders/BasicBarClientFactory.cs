// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders;

public class BasicBarClientFactory(BuildAssetRegistryContext context) : IBasicBarClientFactory
{
    private readonly BuildAssetRegistryContext _context = context;

    public Task<IBasicBarClient> GetBasicBarClient(ILogger logger)
        => Task.FromResult<IBasicBarClient>(new MaestroDbBarClient(_context));
}
