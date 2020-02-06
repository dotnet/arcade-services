// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("add-build-to-channel", HelpText = "Add a build to a channel.")]
    internal class AddBuildToChannelCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "BAR id of build to assign to channel.")]
        public int Id { get; set; }

        [Option("channel", Required = true, HelpText = "Channel to assign build to.")]
        public string Channel { get; set; }

        [Option("source-branch", HelpText = "Branch that should be used as base for the promotion build.")]
        public string SourceBranch { get; set; }

        [Option("source-sha", HelpText = "SHA that should be used as base for the promotion build.")]
        public string SourceSHA { get; set; }

        [Option("validate-signing", HelpText = "Perform signing validation.")]
        public bool DoSigningValidation { get; set; }

        [Option("validate-nuget", HelpText = "Perform NuGet metadata validation.")]
        public bool DoNuGetValidation { get; set; }

        [Option("validate-sourcelink", HelpText = "Perform SourceLink validation.")]
        public bool DoSourcelinkValidation { get; set; }

        [Option("validate-SDL", HelpText = "Perform SDL validation.")]
        public bool DoSDLValidation { get; set; }

        [Option("sdl-validation-parameters", HelpText = "Custom parameters for SDL validation.")]
        public string SDLValidationParams { get; set; }

        [Option("sdl-validation-continue-on-error", HelpText = "Ignore SDL validation errors.")]
        public string SDLValidationContinueOnError { get; set; }

        [Option("skip-assets-publishing", HelpText = "Add the build to the channel without publishing assets to the channel's feeds.")]
        public bool SkipAssetsPublishing { get; set; }

        public override Operation GetOperation()
        {
            return new AddBuildToChannelOperation(this);
        }
    }
}
