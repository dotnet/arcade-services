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
        [RedactFromLogging]
        public int Id { get; set; }

        [Option("publishing-infra-version", Default = 2, Required = false, HelpText = "Which version of the publishing infrastructure should be used.")]
        public int PublishingInfraVersion { get; set; }

        [Option("channel", HelpText = "Channel to assign build to. Required if --default-channels is not specified.")]
        public string Channel { get; set; }

        [Option("default-channels", HelpText = "Assign build to all default channels. Required if --channel is not specified.")]
        public bool AddToDefaultChannels { get; set; }

        [Option("source-branch", HelpText = "Branch that should be used as base for the promotion build. Required if source-sha is specified.")]
        public string SourceBranch { get; set; }

        [Option("source-sha", HelpText = "SHA that should be used as base for the promotion build.")]
        [RedactFromLogging]
        public string SourceSHA { get; set; }

        [Option("validate-signing", HelpText = "Perform signing validation.")]
        public bool DoSigningValidation { get; set; }

        [Option("signing-validation-parameters", Default ="''", HelpText = "Additional (MSBuild) properties to be passed to signing validation.")]
        public string SigningValidationAdditionalParameters { get; set; }

        [Option("validate-nuget", HelpText = "Perform NuGet metadata validation.")]
        public bool DoNuGetValidation { get; set; }

        [Option("validate-sourcelink", HelpText = "Perform SourceLink validation.")]
        public bool DoSourcelinkValidation { get; set; }

        [Option("validate-SDL", HelpText = "Perform SDL validation.")]
        public bool DoSDLValidation { get; set; }

        [Option("sdl-validation-parameters", Default = "''", HelpText = "Custom parameters for SDL validation.")]
        public string SDLValidationParams { get; set; }

        [Option("sdl-validation-continue-on-error", HelpText = "Ignore SDL validation errors.")]
        public string SDLValidationContinueOnError { get; set; }

        [Option("symbol-publishing-parameters", Default = "''", HelpText = "Additional (MSBuild) properties to be passed to symbol publishing")]
        [RedactFromLogging]
        public string SymbolPublishingAdditionalParameters { get; set; }

        [Option("artifact-publishing-parameters", Default = "''", HelpText = "Additional (MSBuild) properties to be passed to asset publishing.")]
        [RedactFromLogging]
        public string ArtifactPublishingAdditionalParameters { get; set; }

        [Option("publish-installers-and-checksums", HelpText = "Whether installers and checksums should be published.")]
        public bool PublishInstallersAndChecksums { get; set; }

        [Option("skip-assets-publishing", HelpText = "Add the build to the channel without publishing assets to the channel's feeds.")]
        public bool SkipAssetsPublishing { get; set; }

        [Option("no-wait", HelpText = "If set, Darc won't wait for the asset publishing and channel assignment. The operation continues asynchronously in AzDO.")]
        public bool NoWait { get; set; }

        public override Operation GetOperation()
        {
            return new AddBuildToChannelOperation(this);
        }
    }
}
