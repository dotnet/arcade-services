// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models;

public class Channel : ExternallySyncedEntity<string>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    [Required]
    public string Classification { get; set; }

    public List<BuildChannel> BuildChannels { get; set; }
    public List<DefaultChannel> DefaultChannels { get; set; }

    public Namespace Namespace { get; set; }

    public string UniqueId => Name;
}
