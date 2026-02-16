// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable disable
namespace BuildInsights.Utilities.AzureDevOps.Models;

public class AzureDevOpsSettingsCollection
{
    public List<AzureDevOpsSettings> Settings { get; set; }
}

public class AzureDevOpsSettings
{
    public string CollectionUri { get; set; }
    public string AccessToken { get; set; }
    public string OrgId { get; set; }
}
