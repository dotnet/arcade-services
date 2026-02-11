// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildInsights.Data.Models;

public class BuildProcessingStatusEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(512)]
    public required string Repository { get; set; }

    public required int BuildId { get; set; }

    [MaxLength(64)]
    public required BuildProcessingStatus Status { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
}

public record BuildProcessingStatus(string Value)
{
    public static BuildProcessingStatus InProcess => new("InProcess");
    public static BuildProcessingStatus Completed => new("Completed");
    public static BuildProcessingStatus ConclusionOverridenByUser => new("ConclusionOverridenByUser");

    public static BuildProcessingStatus FromString(string status) => new(status);
    public override string ToString() => Value;
}
