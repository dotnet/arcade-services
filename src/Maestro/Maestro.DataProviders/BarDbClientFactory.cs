// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders;

public class BarDbClientFactory : IBarDbClientFactory
{
    private readonly BuildAssetRegistryContext _context;

    public BarDbClientFactory(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    public Task<IBarDbClient> GetBarDbClient(ILogger logger)
        => Task.FromResult<IBarDbClient>(new MaestroDbBarClient(_context));
}
