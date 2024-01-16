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

internal class UpdateBuildOperation : Operation
{
    private readonly UpdateBuildCommandLineOptions _options;
    public UpdateBuildOperation(UpdateBuildCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (!(_options.Released ^ _options.NotReleased))
        {
            Console.WriteLine("Please specify either --released or --not-released.");
            return Constants.ErrorCode;
        }

        try
        {
            IBarApiClient barClient = Provider.GetRequiredService<IBarApiClient>();

            Build updatedBuild = await barClient.UpdateBuildAsync(_options.Id, new BuildUpdate { Released = _options.Released });

            Console.WriteLine($"Updated build {_options.Id} with new information.");
            Console.WriteLine(UxHelpers.GetTextBuildDescription(updatedBuild));
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Error: Failed to update build with id '{_options.Id}'");
            return Constants.ErrorCode;
        }

        return Constants.SuccessCode;
    }
}
