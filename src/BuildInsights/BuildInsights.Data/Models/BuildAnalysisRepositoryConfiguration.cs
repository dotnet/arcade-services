// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildInsights.Data.Models;

public class BuildAnalysisRepositoryConfiguration
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(512)]
    public required string Repository { get; set; }

    [MaxLength(256)]
    public required string Branch { get; set; }

    public bool ShouldMergeOnFailureWithKnownIssues { get; set; }
}
