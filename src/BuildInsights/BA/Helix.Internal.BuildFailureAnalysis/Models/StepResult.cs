using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class StepResult : IResult
    {
        public string StepName { get; set; }

        public string JobId { get; set; }

        /// <summary>
        /// All the errors that belongs to this step
        /// </summary>
        public List<Error> Errors { get; set; } = new List<Error>();
        public FailureRate FailureRate { get; set; }

        public List<string> StepHierarchy { get; set; } = new List<string>();

        public IImmutableList<KnownIssue> KnownIssues { get; set; } = ImmutableList<KnownIssue>.Empty;
        public string LinkLog { get; set; }
        public string LinkToStep { get; set; }
        public DateTimeOffset? StepStartTime { get; set; }
    }

    public class Error
    {
        public string ErrorMessage { get; set; }
        public string LinkLog { get; set; }
    }
}
