// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Jobs.Jobs;

public class TextJob : Job
{
    public required string Text { get; init; }
    public override string Type => nameof(TextJob);
}
