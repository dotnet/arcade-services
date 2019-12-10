// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-goal", HelpText = "Gets Goal in minutes for Definition in a Channel")]
    internal class GetGoalCommandLineOptions : CommandLineOptions
    {
        [Option('c', "channel", Required = true, HelpText = "Name of channel Eg : .Net Core 5 Dev ")]
        public string Channel { get; set; }

        [Option('d', "definition-id", Required = true, HelpText = "Azure DevOps Definition Id.")]
        public int DefinitionId { get; set; }

        public override Operation GetOperation()
        {
            return new GetGoalOperation(this);
        }
    }
}
