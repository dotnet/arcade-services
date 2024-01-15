// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteDefaultChannelOperation : UpdateDefaultChannelBaseOperation
{
    private readonly DeleteDefaultChannelCommandLineOptions _options;
    public DeleteDefaultChannelOperation(DeleteDefaultChannelCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IBarApiClient barClient = Provider.GetRequiredService<IBarApiClient>();

            DefaultChannel resolvedChannel = await ResolveSingleChannel();
            if (resolvedChannel == null)
            {
                return Constants.ErrorCode;
            }

            await barClient.DeleteDefaultChannelAsync(resolvedChannel.Id);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error: Failed remove the default channel association.");
            return Constants.ErrorCode;
        }
    }
}
