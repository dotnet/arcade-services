// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models;

public class Namespace
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public List<Subscription> Subscriptions { get; set; }
    public List<Channel> Channels { get; set; }
    public List<DefaultChannel> DefaultChannels { get; set; }
    public List<RepositoryBranch> RepositoryBranches { get; set; }
}
