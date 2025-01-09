// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class VerifyOperation : Operation
{
    private readonly VerifyCommandLineOptions _options;
    private readonly ILogger<VerifyOperation> _logger;

    public VerifyOperation(
        VerifyCommandLineOptions options,
        ILogger<VerifyOperation> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Verify that the repository has a correct dependency structure.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        var local = new Local(_options.GetRemoteTokenProvider(), _logger);

        try
        {
            if (!await local.Verify())
            {
                Console.WriteLine("Dependency verification failed.");
                return Constants.ErrorCode;
            }
            Console.WriteLine("Dependency verification succeeded.");
            return Constants.SuccessCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to verify repository dependency state.");
            return Constants.ErrorCode;
        }
    }
}
