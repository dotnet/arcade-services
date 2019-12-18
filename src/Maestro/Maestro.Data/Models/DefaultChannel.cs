// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Services.Utility;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models
{
    public class DefaultChannel
    {
        private string _repository;

        public int Id { get; set; }

        [StringLength(300)]
        [Column(TypeName = "varchar(300)")]
        [Required]
        public string Repository
        {
            get
            {
                return AzureDevOpsClient.NormalizeUrl(_repository);
            }

            set
            {
                _repository = AzureDevOpsClient.NormalizeUrl(value);
            }
        }

        private string _branch;

        [StringLength(100)]
        [Column(TypeName = "varchar(100)")]
        [Required]
        public string Branch
        {
            get
            {
                return GitHelpers.NormalizeBranchName(_branch);
            }
            set
            {
                _branch = GitHelpers.NormalizeBranchName(value);
            }
        }

        [Required]
        public int ChannelId { get; set; }

        public bool Enabled { get; set; } = true;

        public Channel Channel { get; set; }
    }
}
