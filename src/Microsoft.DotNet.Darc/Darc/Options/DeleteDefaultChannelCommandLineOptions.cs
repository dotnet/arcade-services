// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("delete-default-channel", HelpText = "Remove a default channel association.")]
internal class DeleteDefaultChannelCommandLineOptions : UpdateDefaultChannelBaseCommandLineOptions<DeleteDefaultChannelOperation>, IUpdateDefaultChannelBaseCommandLineOptions
{
    public override DeleteDefaultChannelOperation GetOperation(ServiceProvider sp)
    {
        return ActivatorUtilities.CreateInstance<DeleteDefaultChannelOperation>(sp, this);
    }
}
