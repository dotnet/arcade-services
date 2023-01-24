// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class PushOperation : Operation
{
    private readonly VmrPushCommandLineOptions _options;

    public PushOperation(VmrPushCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrPusher = Provider.GetRequiredService<IVmrPusher>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);
        
        if (_options.VerifyCommits && _options.CommitVerificationPat == null)
        {
            Logger.LogError("Please use --commit-verification-pat to specify a GitHub token with basic scope to be used for authenticating to GitHub GraphQL API");
            return Constants.ErrorCode;
        }
        
        try
        {
            await vmrPusher.Push(_options.Remote, _options.Branch, _options.VerifyCommits, _options.CommitVerificationPat, listener.Token);
            return 0;
        }
        catch(Exception e)
        {
            Logger.LogError(
                    "Pushing to the VMR failed. {exception}",
                    Environment.NewLine + e.Message);

            Logger.LogDebug("{exception}", e);

            return Constants.ErrorCode;
        }
    }
}
