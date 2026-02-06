// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.Internal.Helix.GitHub.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class MarkdownParameters
    {
        public MergedBuildResultAnalysis Analysis { get; }
        public KnownIssueUrlOptions KnownIssueUrlOptions { get; }
        public MarkdownSummarizeInstructions SummarizeInstructions { get; }
        public Repository Repository { get; }
        public string SnapshotId { get; }
        public string PullRequest { get; set; }

        public MarkdownParameters(
            MergedBuildResultAnalysis analysis,
            string snapshotId,
            string pullRequest,
            Repository repository,
            KnownIssueUrlOptions knownIssueUrlOptions = null,
            MarkdownSummarizeInstructions summarizeInstructions = null)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException(nameof(analysis));
            }

            if (repository == null || string.IsNullOrEmpty(repository.Id))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(repository.Id));
            }

            if (string.IsNullOrEmpty(snapshotId))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(snapshotId));
            }

            Analysis = analysis;
            Repository = repository;
            PullRequest = pullRequest;
            SnapshotId = snapshotId;
            KnownIssueUrlOptions = knownIssueUrlOptions;
            SummarizeInstructions = summarizeInstructions;
        }
    }
}
