// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models
{
    public class DependencyFlowEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [MaxLength(Repository.RepositoryNameLength)]
        public string SourceRepository { get; set; }

        [MaxLength(Repository.RepositoryNameLength)]
        public string TargetRepository { get; set; }

        public int? ChannelId { get; set; }

        public int BuildId { get; set; }

        public Build Build { get; set; }

        /// <summary>
        ///     The dependency flow PR/Branch event: Created, Updated, Completed
        /// </summary>
        public string Event { get; set; }

        /// <summary>
        ///     The reason for the event: New (Created), Merged Automatically, Merged Manually,
        ///         Closed Manually, PR failed (for Updates), etc
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        ///     The type of dependency flow: PR or Branch
        /// </summary>
        public string FlowType { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public string Url { get; set; }
    }
}
