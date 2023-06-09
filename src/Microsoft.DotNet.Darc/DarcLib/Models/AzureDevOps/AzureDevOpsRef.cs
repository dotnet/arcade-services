// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsRef
{
    public AzureDevOpsRef(string name, string sha, string oldObjectId = null)
    {
        Name = name;
        NewObjectId = sha;

        if (!string.IsNullOrEmpty(oldObjectId))
        {
            OldObjectId = oldObjectId;
        }
    }

    public string Name { get; set; }

    public string NewObjectId { get; set; }

    public string OldObjectId { get; set; }
}
