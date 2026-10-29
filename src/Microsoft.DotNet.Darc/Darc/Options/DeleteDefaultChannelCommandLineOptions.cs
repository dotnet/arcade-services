// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

internal class DeleteDefaultChannelCommandLineOptions : UpdateDefaultChannelBaseCommandLineOptions<DeleteDefaultChannelOperation>, IUpdateDefaultChannelBaseCommandLineOptions
{
    public override string Verb => "delete-default-channel";

    public override string VerbDescription => "Remove a default channel association.";

    public override DeleteDefaultChannelOperation GetOperation(ServiceProvider sp)
    {
        return ActivatorUtilities.CreateInstance<DeleteDefaultChannelOperation>(sp, this);
    }
}
