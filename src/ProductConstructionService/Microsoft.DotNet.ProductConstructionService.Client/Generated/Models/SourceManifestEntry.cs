// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    /// <summary>
    /// Represents an entry in the source manifest for a VMR build
    /// </summary>
    public class SourceManifestEntry
    {
        public SourceManifestEntry(string path, string remoteUri, string commitSha, int? barId)
        {
            Path = path;
            RemoteUri = remoteUri;
            CommitSha = commitSha;
            BarId = barId;
        }

        /// <summary>
        /// Path where the component is located in the VMR
        /// </summary>
        [Required]
        public string Path { get; set; }

        /// <summary>
        /// URI from which the component has been synchronized
        /// </summary>
        [Required]
        public string RemoteUri { get; set; }

        /// <summary>
        /// Original commit SHA from which the component has been synchronized
        /// </summary>
        [Required]
        public string CommitSha { get; set; }

        /// <summary>
        /// BAR ID of the build, if available
        /// </summary>
        public int? BarId { get; set; }
    }
}