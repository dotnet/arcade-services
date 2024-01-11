// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Helpers;

internal class BarApiClientFactory : IBarApiClientFactory
{
    private readonly CommandLineOptions _options;

    public BarApiClientFactory(CommandLineOptions options)
    {
        _options = options;
    }

    public static IBarClient GetBarClient(CommandLineOptions options, ILogger logger)
    {
        DarcSettings darcSettings = LocalSettings.GetDarcSettings(options, logger);
        IBarClient barClient = null;
        if (!string.IsNullOrEmpty(darcSettings.BuildAssetRegistryPassword))
        {
            barClient = new MaestroApiBarClient(darcSettings.BuildAssetRegistryPassword,
            darcSettings.BuildAssetRegistryBaseUri);
        }

        return barClient;
    }

    public Task<IBarClient> GetBarClientAsync(ILogger logger)
        => Task.FromResult(GetBarClient(_options, logger));
}
