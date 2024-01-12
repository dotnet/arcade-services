// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public interface IBarApiClientFactory
{
    Task<IBarApiClient> GetBarClientAsync(ILogger logger);
}
