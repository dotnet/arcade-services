// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public interface ICoherencyUpdateResolverFactory
{
    Task<ICoherencyUpdateResolver> CreateAsync(ILogger logger);
}

public class CoherencyUpdateResolverFactory(IRemoteFactory remoteFactory)
    : ICoherencyUpdateResolverFactory
{
    public async Task<ICoherencyUpdateResolver> CreateAsync(ILogger logger)
        => new CoherencyUpdateResolver(await remoteFactory.GetBarClientAsync(logger), logger);
}
