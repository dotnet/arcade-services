// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data.Helpers;
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
                return _repository;
            }

            set
            {
                _repository = NormalizationOperation.RemoveUserFromUrl(value);
            }
        }

        [StringLength(100)]
        [Column(TypeName = "varchar(100)")]
        [Required]
        public string Branch { get; set; }

        [Required]
        public int ChannelId { get; set; }

        public Channel Channel { get; set; }
    }
}
