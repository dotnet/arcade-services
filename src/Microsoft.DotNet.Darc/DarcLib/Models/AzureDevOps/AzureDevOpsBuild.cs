// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsBuild
{
    public long Id { get; set; }

    public string BuildNumber { get; set; }

    public AzureDevOpsBuildDefinition Definition { get; set; }

    public AzureDevOpsProject Project { get; set; }

    public string Status { get; set; }

    public string Result { get; set; }
}
