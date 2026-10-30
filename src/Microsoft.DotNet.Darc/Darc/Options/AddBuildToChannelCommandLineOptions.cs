// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AddBuildToChannelCommandLineOptions : CommandLineOptions<AddBuildToChannelOperation>
{
    public override string Verb => "add-build-to-channel";

    public override string VerbDescription => "Add a build to a channel.";

    [RedactFromLogging]
    public int Id { get; set; }

    public int PublishingInfraVersion { get; set; }

    public string Channel { get; set; }

    public bool AddToDefaultChannels { get; set; }

    public bool DefaultChannelsRequired { get; set; }

    public string SourceBranch { get; set; }

    [RedactFromLogging]
    public string SourceSHA { get; set; }

    public bool DoSigningValidation { get; set; }

    public string SigningValidationAdditionalParameters { get; set; }

    public bool DoNuGetValidation { get; set; }

    public bool DoSourcelinkValidation { get; set; }

    public bool DoSDLValidation { get; set; }

    public string SDLValidationParams { get; set; }

    public string SDLValidationContinueOnError { get; set; }

    [RedactFromLogging]
    public string SymbolPublishingAdditionalParameters { get; set; }

    [RedactFromLogging]
    public string ArtifactPublishingAdditionalParameters { get; set; }

    public bool PublishInstallersAndChecksums { get; set; }

    public bool SkipAssetsPublishing { get; set; }

    public bool NoWait { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "BAR id of build to assign to channel.", value => Id = value, required: true);
        Add<int>("--publishing-infra-version", "Which version of the publishing infrastructure should be used.", value => PublishingInfraVersion = value, defaultValue: () => 3);
        Add<string>("--channel", "Channel to assign build to. Required if --default-channels is not specified.", value => Channel = value);
        Add<bool>("--default-channels", "Assign build to all default channels. Required if --channel is not specified.", value => AddToDefaultChannels = value);
        Add<bool>("--default-channels-required", "Requires existence of enabled default channel association for a build branch. Applies only if --default-channels specified.", value => DefaultChannelsRequired = value);
        Add<string>("--source-branch", "Branch that should be used as base for the promotion build. Required if source-sha is specified.", value => SourceBranch = value, defaultValue: () => "main");
        Add<string>("--source-sha", "SHA that should be used as base for the promotion build.", value => SourceSHA = value);
        Add<bool>("--validate-signing", "Perform signing validation.", value => DoSigningValidation = value);
        Add<string>("--signing-validation-parameters", "Additional (MSBuild) properties to be passed to signing validation.", value => SigningValidationAdditionalParameters = value, defaultValue: () => "''");
        Add<bool>("--validate-nuget", "Perform NuGet metadata validation.", value => DoNuGetValidation = value);
        Add<bool>("--validate-sourcelink", "Perform SourceLink validation.", value => DoSourcelinkValidation = value);
        Add<bool>("--validate-SDL", "Perform SDL validation.", value => DoSDLValidation = value);
        Add<string>("--sdl-validation-parameters", "Custom parameters for SDL validation.", value => SDLValidationParams = value, defaultValue: () => "''");
        Add<string>("--sdl-validation-continue-on-error", "Ignore SDL validation errors.", value => SDLValidationContinueOnError = value);
        Add<string>("--symbol-publishing-parameters", "Additional (MSBuild) properties to be passed to symbol publishing", value => SymbolPublishingAdditionalParameters = value, defaultValue: () => "''");
        Add<string>("--artifact-publishing-parameters", "Additional (MSBuild) properties to be passed to asset publishing.", value => ArtifactPublishingAdditionalParameters = value, defaultValue: () => "''");
        Add<bool>("--publish-installers-and-checksums", "Whether installers and checksums should be published. This option is ignored", value => PublishInstallersAndChecksums = value);
        Add<bool>("--skip-assets-publishing", "Add the build to the channel without publishing assets to the channel's feeds.", value => SkipAssetsPublishing = value);
        Add<bool>("--no-wait", "If set, Darc won't wait for the asset publishing and channel assignment. The operation continues asynchronously in AzDO.", value => NoWait = value);
        base.Configure();
    }
}
