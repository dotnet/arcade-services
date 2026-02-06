// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class BuildAnalysisRepositoryConfiguration : ITableEntity
    {
        public BuildAnalysisRepositoryConfiguration() { }
        public BuildAnalysisRepositoryConfiguration(string repository, string branch, bool shouldMergeOnFailureWithKnownIssues)
        {
            PartitionKey = repository;
            RowKey = branch;
            ShouldMergeOnFailureWithKnownIssues = shouldMergeOnFailureWithKnownIssues;
        }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public bool ShouldMergeOnFailureWithKnownIssues { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
