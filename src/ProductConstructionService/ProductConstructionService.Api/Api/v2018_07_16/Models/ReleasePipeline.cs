// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class ReleasePipeline
{
    public int Id { get; set; }

    public int PipelineIdentifier { get; set; }

    public string Organization { get; set; }

    public string Project { get; set; }
}
