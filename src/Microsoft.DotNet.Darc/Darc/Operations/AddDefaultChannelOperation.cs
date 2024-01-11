// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddDefaultChannelOperation : Operation
{
    private readonly AddDefaultChannelCommandLineOptions _options;

    public AddDefaultChannelOperation(AddDefaultChannelCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IRemote repoRemote = RemoteFactory.GetRemote(_options, _options.Repository, Logger);
            IBarClient barClient = BarApiClientFactory.GetBarClient(_options, Logger);

            // Users can ignore the flag and pass in -regex: but to prevent typos we'll avoid that.
            _options.Branch = _options.UseBranchAsRegex ? $"-regex:{_options.Branch}" : GitHelpers.NormalizeBranchName(_options.Branch);

            if (!(await UxHelpers.VerifyAndConfirmBranchExistsAsync(repoRemote, _options.Repository, _options.Branch, !_options.NoConfirmation)))
            {
                Console.WriteLine("Aborting default channel creation.");
                return Constants.ErrorCode;
            }

            await barClient.AddDefaultChannelAsync(_options.Repository, _options.Branch, _options.Channel);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error: Failed to add a new default channel association.");
            return Constants.ErrorCode;
        }
    }
}
