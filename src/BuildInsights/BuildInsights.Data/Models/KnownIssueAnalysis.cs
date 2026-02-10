// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildInsights.Data.Models;

public class KnownIssueAnalysis
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string IssueId { get; set; }

    public required int BuildId { get; set; }

    public required string ErrorMessage { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
}
