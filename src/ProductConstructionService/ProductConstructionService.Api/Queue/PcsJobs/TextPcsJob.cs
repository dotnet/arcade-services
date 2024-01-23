// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue.WorkItems;

public class TextPcsJob : PcsJob
{
    public string Text { get; set; } = string.Empty;
}
