// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc
{
    public class DownloadedBuild
    {
        public Build Build { get; set; }
        public bool Successful { get; set; }
        public IEnumerable<DownloadedAsset> DownloadedAssets { get; set; }
        /// <summary>
        ///     Root output directory for this build.
        /// </summary>
        public string ReleaseLayoutOutputDirectory { get; set; }
        /// <summary>
        ///     True if the output has any shipping assets.
        /// </summary>
        public bool AnyShippingAssets { get; set; }
    }
}
