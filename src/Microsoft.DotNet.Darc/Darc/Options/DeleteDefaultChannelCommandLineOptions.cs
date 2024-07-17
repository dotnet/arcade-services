// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("delete-default-channel", HelpText = "Remove a default channel association.")]
internal class DeleteDefaultChannelCommandLineOptions : DefaultChannelStatusCommandLineOptions
{
    public override Type GetOperation() => typeof(DeleteDefaultChannelOperation);
}
