// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class PushOperation : Operation
{
    private readonly VmrPushCommandLineOptions _options;
    private readonly IVmrPusher _vmrPusher;
    private readonly ILogger<PushOperation> _logger;

    public PushOperation(
        VmrPushCommandLineOptions options,
        IVmrPusher vmrPusher,
        ILogger<PushOperation> logger)
    {
        _options = options;
        _vmrPusher = vmrPusher;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        using var listener = CancellationKeyListener.ListenForCancellation(_logger);

        if (!_options.SkipCommitVerification && _options.CommitVerificationPat == null)
        {
            _logger.LogError("Please use --commit-verification-pat to specify a GitHub token with basic scope to be used for authenticating to GitHub GraphQL API");
            return Constants.ErrorCode;
        }

        try
        {
            await _vmrPusher.Push(_options.RemoteUrl, _options.Branch, _options.SkipCommitVerification, _options.CommitVerificationPat, listener.Token);
            return 0;
        }
        catch (Exception e)
        {
            _logger.LogError(
                    "Pushing to the VMR failed. {exception}",
                    Environment.NewLine + e.Message);

            _logger.LogDebug("{exception}", e);

            return Constants.ErrorCode;
        }
    }
}
