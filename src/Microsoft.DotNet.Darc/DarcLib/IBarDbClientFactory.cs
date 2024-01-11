// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Factory for a client that accessed the BAR database directly.
/// </summary>
public interface IBarDbClientFactory
{
    Task<IBarDbClient> GetBarDbClient(ILogger logger);
}
