// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models;

public class Asset
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [StringLength(250)]
    public string Name { get; set; }

    [StringLength(75)]
    public string Version { get; set; }

    public int BuildId { get; set; }

    public bool NonShipping { get; set; }

    public List<AssetLocation> Locations { get; set; }
}
