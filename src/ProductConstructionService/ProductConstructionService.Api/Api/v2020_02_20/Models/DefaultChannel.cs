// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2020_02_20.Models;

public class DefaultChannel
{
    public DefaultChannel(Maestro.Data.Models.DefaultChannel other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Repository = other.Repository;
        Branch = other.Branch;
        Channel = other.Channel == null ? null : new Channel(other.Channel);
        Enabled = other.Enabled;
    }

    public int Id { get; set; }

    [StringLength(300)]
    [Required]
    public string Repository { get; set; }

    [StringLength(100)]
    public string Branch { get; set; }

    public Channel Channel { get; set; }

    public bool Enabled { get; set; }

    public class DefaultChannelCreateData
    {
        [StringLength(300)]
        [Required]
        public string Repository { get; set; }

        [StringLength(100)]
        [Required]
        public string Branch { get; set; }

        [Required]
        public int ChannelId { get; set; }

        public bool? Enabled { get; set; }
    }

    public class DefaultChannelUpdateData
    {
        [StringLength(300)]
        public string Repository { get; set; }

        [StringLength(100)]
        public string Branch { get; set; }
        public int? ChannelId { get; set; }
        public bool? Enabled { get; set; }
    }
}
