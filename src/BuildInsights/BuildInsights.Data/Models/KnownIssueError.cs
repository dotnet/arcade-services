// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;

namespace BuildInsights.Data.Models;

public class KnownIssueError
{
    [MaxLength(500)]
    public required string Repository { get; set; }

    [MaxLength(100)]
    public required string IssueId { get; set; }

    public required string ErrorMessage { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
}
