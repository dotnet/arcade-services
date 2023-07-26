// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

public class DownloadedAsset
{
    /// <summary>
    /// Asset that was downloaded.
    /// </summary>
    public Asset Asset { get; set; }
    /// <summary>
    /// Source location (uri) where the asset came from.
    /// </summary>
    public string SourceLocation { get; set; }
    /// <summary>
    /// Target location where the asset was downloaded to for the release style layout
    /// </summary>
    public string SeparatedLayoutTargetLocation { get; set; }
    /// <summary>
    /// Target location where the asset was downloaded to for the unified-style (sans repo/build #) layout
    /// </summary>
    public string UnifiedLayoutTargetLocation { get; set; }
    /// <summary>
    /// True if the asset download was successful. If false, Asset is the only valid property
    /// </summary>
    public string ReleasePackageLayoutTargetLocation { get; set; }
    public bool Successful { get; set; }
    /// <summary>
    /// Location type of the asset that actually got downloaded.
    /// </summary>
    public LocationType LocationType { get; set; }
    /// <summary>
    /// Size of the artifact in bytes
    /// </summary>
    public long SizeInBytes { get; set; }
    public string FileHash { get; set; }

    public List<DownloadedSubAsset> SubAssets { get; set; } = new List<DownloadedSubAsset>();
}
