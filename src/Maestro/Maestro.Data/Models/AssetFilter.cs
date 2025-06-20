// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Maestro.Data.Models;

public class AssetFilter
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Filter for assets to ignore, e.g. "Microsoft.AspNetCore.App", "Microsoft.NETCore.*", etc.
    /// </summary>
    public string Filter { get; set; }
}
