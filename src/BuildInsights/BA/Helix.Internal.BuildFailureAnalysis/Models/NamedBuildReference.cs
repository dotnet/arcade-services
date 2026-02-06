// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public class NamedBuildReference : BuildReferenceIdentifier
    {
        public string Name { get; }
        public string WebUrl { get; }

        public NamedBuildReference(
            string name,
            string webUrl,
            string org,
            string project,
            int buildId,
            string buildUrl,
            int definitionId,
            string definitionName,
            string repositoryId,
            string sourceSha,
            string targetBranch,
            bool isCompleted = false) : base(org, project, buildId, buildUrl, definitionId, definitionName, repositoryId, sourceSha, targetBranch, isCompleted)
        {
            Name = name;
            WebUrl = webUrl;
        }
    }
}
