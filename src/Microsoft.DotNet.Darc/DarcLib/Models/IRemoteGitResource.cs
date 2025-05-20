// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models;

/// <summary>
/// Represents resources for which we use ETag caching via the Github API.
/// Using ETags when requesting resources from Github improves speed and reduces rate-limiting issues.
/// </summary>
public interface ICachableGitResource
{
    public string ETag { get; set; }
}
