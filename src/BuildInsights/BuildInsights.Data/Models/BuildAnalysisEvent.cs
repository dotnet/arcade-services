// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildInsights.Data.Models;

public class BuildAnalysisEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(256)]
    public required string PipelineName { get; set; }

    public required int BuildId { get; set; }

    [MaxLength(512)]
    public required string Repository { get; set; }

    [MaxLength(256)]
    public required string Project { get; set; }

    public bool IsRepositorySupported { get; set; }

    public DateTimeOffset? AnalysisTimestamp { get; set; }
}
