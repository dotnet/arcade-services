// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal interface IUpdateDefaultChannelBaseCommandLineOptions : ICommandLineOptions, IConfigurationManagementCommandLineOptions
{
    string Branch { get; set; }
    string Channel { get; set; }
    int Id { get; set; }
    string Repository { get; set; }
}

internal abstract class UpdateDefaultChannelBaseCommandLineOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T> : ConfigurationManagementCommandLineOptions<T>, IUpdateDefaultChannelBaseCommandLineOptions where T : Operation
{
    public int Id { get; set; }

    public string Channel { get; set; }

    public string Branch { get; set; }

    public string Repository { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "Existing default channel id", value => Id = value, defaultValue: () => -1);
        Add<string>("--channel", "Existing default channel association target channel name.", value => Channel = value);
        Add<string>("--branch", "Existing default channel association source branch name.", value => Branch = value);
        Add<string>("--repo", "Existing default channel association source repository name.", value => Repository = value);
        base.Configure();
    }
}
