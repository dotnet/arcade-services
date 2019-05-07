// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("delete-default-channel", HelpText = "Remove a default channel association.")]
    internal class DeleteDefaultChannelCommandLineOptions : DefaultChannelStatusCommandLineOptions
    {
        public override Operation GetOperation()
        {
            return new DeleteDefaultChannelOperation(this);
        }
    }
}
