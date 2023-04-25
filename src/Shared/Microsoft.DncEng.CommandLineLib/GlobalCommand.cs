// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Mono.Options;

namespace Microsoft.DncEng.CommandLineLib;

public class GlobalCommand : Command
{
    public override OptionSet GetOptions()
    {
        return new OptionSet
        {
            {
                "verbose|v", "Increase logging verbosity up to 2 (default 1)",
                v => Verbosity = BumpVerbosity(Verbosity)
            },
            {
                "quiet|q", "Suppress most output (except critical failures or prompts)",
                q => Verbosity = VerbosityLevel.Quiet
            },
            {"help|h|?", "Show help", h => Help = true},
        };
    }

    public bool Help { get; set; }
    public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

    private static VerbosityLevel BumpVerbosity(VerbosityLevel verbosity)
    {
        verbosity++;
        if (verbosity > VerbosityLevel.Verbose)
        {
            verbosity = VerbosityLevel.Verbose;
        }

        return verbosity;
    }
}
